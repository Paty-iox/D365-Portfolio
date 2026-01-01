using System;
using System.IO;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace ApexClaims.Plugins
{
    // Populates weather conditions on Create/Update of new_claim
    // Register: Post-Operation, Asynchronous, filter on new_incidentlatitude, new_incidentlongitude, new_incidentdate
    // Execution Order: 2 (runs after ClaimGeocoder)
    // Note: Async avoids blocking user saves during external API calls
    public class ClaimWeather : IPlugin
    {
        // Fallback values for development (Environment Variables override these)
        private const string DefaultWeatherApiUrl = "https://ApexClaims-func.azurewebsites.net/api/WeatherLookup";
        private const string DefaultWeatherApiKey = "YOUR_FUNCTION_KEY_HERE";
        private const int ApiTimeoutMs = 15000;

        // Environment Variable schema names
        private const string EnvVarWeatherUrl = "new_weatherapiurl";
        private const string EnvVarWeatherKey = "new_weatherapikey";

        // Field names
        private const string IncidentLatitudeField = "new_incidentlatitude";
        private const string IncidentLongitudeField = "new_incidentlongitude";
        private const string IncidentDateField = "new_incidentdate";
        private const string WeatherConditionsField = "new_weatherconditions";
        private const string ClaimEntityName = "new_claim";

        public void Execute(IServiceProvider serviceProvider)
        {
            // Get services
            IPluginExecutionContext context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            IOrganizationServiceFactory serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            IOrganizationService service = serviceFactory.CreateOrganizationService(context.UserId);
            ITracingService trace = (ITracingService)serviceProvider.GetService(typeof(ITracingService));

            try
            {
                trace.Trace("ClaimWeather: Plugin execution started");

                // Validate context
                if (!ValidateContext(context, trace))
                {
                    return;
                }

                // Get target entity
                Entity target = (Entity)context.InputParameters["Target"];

                // For Update, only proceed if relevant fields changed
                if (context.MessageName.Equals("Update", StringComparison.OrdinalIgnoreCase))
                {
                    if (!target.Contains(IncidentLatitudeField) &&
                        !target.Contains(IncidentLongitudeField) &&
                        !target.Contains(IncidentDateField))
                    {
                        trace.Trace("ClaimWeather: No relevant fields changed, skipping");
                        return;
                    }
                }

                // Retrieve current record to get all values (including those set by ClaimGeocoder)
                Entity claim = service.Retrieve(ClaimEntityName, target.Id, new ColumnSet(
                    IncidentLatitudeField,
                    IncidentLongitudeField,
                    IncidentDateField,
                    WeatherConditionsField
                ));

                // Get coordinate and date values
                decimal? latitude = claim.GetAttributeValue<decimal?>(IncidentLatitudeField);
                decimal? longitude = claim.GetAttributeValue<decimal?>(IncidentLongitudeField);
                DateTime? incidentDate = claim.GetAttributeValue<DateTime?>(IncidentDateField);

                trace.Trace("ClaimWeather: Checking values - HasLat: {0}, HasLon: {1}, HasDate: {2}",
                    latitude.HasValue, longitude.HasValue, incidentDate.HasValue);

                // Validate coordinates with proper range check (0,0 is valid - Gulf of Guinea)
                if (!latitude.HasValue || !longitude.HasValue)
                {
                    trace.Trace("ClaimWeather: Missing coordinates, skipping weather lookup");
                    return;
                }

                // Validate coordinate ranges
                if (latitude.Value < -90m || latitude.Value > 90m ||
                    longitude.Value < -180m || longitude.Value > 180m)
                {
                    trace.Trace("ClaimWeather: Invalid coordinate range, skipping weather lookup");
                    return;
                }

                if (!incidentDate.HasValue)
                {
                    trace.Trace("ClaimWeather: Missing incident date, skipping weather lookup");
                    return;
                }

                // Check if date is in the future
                if (incidentDate.Value.Date > DateTime.UtcNow.Date)
                {
                    trace.Trace("ClaimWeather: Incident date is in the future, skipping weather lookup");
                    return;
                }

                // Get API configuration from Environment Variables
                string apiUrl = GetEnvironmentVariable(service, EnvVarWeatherUrl, trace) ?? DefaultWeatherApiUrl;
                string apiKey = GetEnvironmentVariable(service, EnvVarWeatherKey, trace) ?? DefaultWeatherApiKey;

                // Call weather API
                WeatherApiResponse weatherResult = CallWeatherApi(apiUrl, apiKey, latitude.Value, longitude.Value, incidentDate.Value, trace);

                // Update record if we got conditions
                if (weatherResult != null && weatherResult.Success && !string.IsNullOrEmpty(weatherResult.Conditions))
                {
                    // Check if value changed before updating
                    string existingConditions = claim.GetAttributeValue<string>(WeatherConditionsField);
                    if (existingConditions != weatherResult.Conditions)
                    {
                        Entity updateEntity = new Entity(ClaimEntityName, target.Id);
                        updateEntity[WeatherConditionsField] = weatherResult.Conditions;
                        service.Update(updateEntity);
                        trace.Trace("ClaimWeather: Weather conditions updated successfully");
                    }
                    else
                    {
                        trace.Trace("ClaimWeather: Weather conditions unchanged, skipping update");
                    }
                }
                else
                {
                    trace.Trace("ClaimWeather: No weather conditions returned - {0}",
                        weatherResult?.Error ?? "API call failed");
                }
            }
            catch (Exception ex)
            {
                // Log the error but don't throw - we don't want to block the user from saving
                trace.Trace("ClaimWeather: Error - {0}", ex.Message);
            }
        }

        private bool ValidateContext(IPluginExecutionContext context, ITracingService trace)
        {
            // Check entity name
            if (!context.PrimaryEntityName.Equals(ClaimEntityName, StringComparison.OrdinalIgnoreCase))
            {
                trace.Trace("ClaimWeather: Wrong entity - {0}", context.PrimaryEntityName);
                return false;
            }

            // Check message
            string message = context.MessageName;
            if (!message.Equals("Create", StringComparison.OrdinalIgnoreCase) &&
                !message.Equals("Update", StringComparison.OrdinalIgnoreCase))
            {
                trace.Trace("ClaimWeather: Wrong message - {0}", message);
                return false;
            }

            // Check for target entity
            if (!context.InputParameters.Contains("Target") || !(context.InputParameters["Target"] is Entity))
            {
                trace.Trace("ClaimWeather: No target entity found");
                return false;
            }

            return true;
        }

        private string GetEnvironmentVariable(IOrganizationService service, string schemaName, ITracingService trace)
        {
            try
            {
                var query = new QueryExpression("environmentvariablevalue")
                {
                    ColumnSet = new ColumnSet("value"),
                    Criteria = new FilterExpression
                    {
                        Conditions =
                        {
                            new ConditionExpression("statecode", ConditionOperator.Equal, 0)
                        }
                    },
                    LinkEntities =
                    {
                        new LinkEntity
                        {
                            LinkFromEntityName = "environmentvariablevalue",
                            LinkToEntityName = "environmentvariabledefinition",
                            LinkFromAttributeName = "environmentvariabledefinitionid",
                            LinkToAttributeName = "environmentvariabledefinitionid",
                            LinkCriteria = new FilterExpression
                            {
                                Conditions =
                                {
                                    new ConditionExpression("schemaname", ConditionOperator.Equal, schemaName)
                                }
                            }
                        }
                    }
                };

                var results = service.RetrieveMultiple(query);
                if (results.Entities.Count > 0)
                {
                    return results.Entities[0].GetAttributeValue<string>("value");
                }
            }
            catch (Exception ex)
            {
                trace.Trace("ClaimWeather: Failed to get environment variable {0} - {1}", schemaName, ex.Message);
            }

            return null;
        }

        private WeatherApiResponse CallWeatherApi(string apiUrl, string apiKey, decimal latitude, decimal longitude, DateTime date, ITracingService trace)
        {
            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(apiUrl);
                request.Method = "POST";
                request.ContentType = "application/json";
                request.Timeout = ApiTimeoutMs;

                // Use header for API key instead of query string
                request.Headers.Add("x-functions-key", apiKey);

                // Build JSON body using serializer
                var requestBody = new WeatherApiRequest
                {
                    Latitude = latitude,
                    Longitude = longitude,
                    Date = date.ToString("yyyy-MM-dd")
                };
                byte[] bodyBytes = SerializeToJson(requestBody);
                request.ContentLength = bodyBytes.Length;

                trace.Trace("ClaimWeather: Sending request to weather API");

                using (Stream requestStream = request.GetRequestStream())
                {
                    requestStream.Write(bodyBytes, 0, bodyBytes.Length);
                }

                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (Stream responseStream = response.GetResponseStream())
                {
                    trace.Trace("ClaimWeather: Response received - Status: {0}", response.StatusCode);
                    return DeserializeFromJson<WeatherApiResponse>(responseStream);
                }
            }
            catch (WebException webEx)
            {
                trace.Trace("ClaimWeather: Web error - {0}", webEx.Message);

                // Try to parse error response
                if (webEx.Response != null)
                {
                    try
                    {
                        using (Stream errorStream = webEx.Response.GetResponseStream())
                        {
                            var errorResponse = DeserializeFromJson<WeatherApiResponse>(errorStream);
                            if (errorResponse != null)
                            {
                                return errorResponse;
                            }
                        }
                    }
                    catch (Exception parseEx)
                    {
                        trace.Trace("ClaimWeather: Failed to parse error response - {0}", parseEx.Message);
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                trace.Trace("ClaimWeather: API call error - {0}", ex.Message);
                return null;
            }
        }

        private byte[] SerializeToJson<T>(T obj)
        {
            var serializer = new DataContractJsonSerializer(typeof(T));
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, obj);
                return stream.ToArray();
            }
        }

        private T DeserializeFromJson<T>(Stream stream)
        {
            var serializer = new DataContractJsonSerializer(typeof(T));
            return (T)serializer.ReadObject(stream);
        }
    }

    #region Weather Data Contracts

    [DataContract]
    internal class WeatherApiRequest
    {
        [DataMember(Name = "latitude")]
        public decimal Latitude { get; set; }

        [DataMember(Name = "longitude")]
        public decimal Longitude { get; set; }

        [DataMember(Name = "date")]
        public string Date { get; set; }
    }

    [DataContract]
    internal class WeatherApiResponse
    {
        [DataMember(Name = "success")]
        public bool Success { get; set; }

        [DataMember(Name = "conditions")]
        public string Conditions { get; set; }

        [DataMember(Name = "error")]
        public string Error { get; set; }
    }

    #endregion
}
