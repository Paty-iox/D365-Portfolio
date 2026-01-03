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
    public class ClaimWeather : IPlugin
    {
        private const int ApiTimeoutMs = 15000;
        private const int MaxRetries = 3;
        private const int BaseRetryDelayMs = 500;

        private const string EnvVarWeatherUrl = "new_weatherapiurl";
        private const string EnvVarWeatherKey = "new_weatherapikey";

        private const string IncidentLatitudeField = "new_incidentlatitude";
        private const string IncidentLongitudeField = "new_incidentlongitude";
        private const string IncidentDateField = "new_incidentdate";
        private const string WeatherConditionsField = "new_weatherconditions";
        private const string ClaimEntityName = "new_claim";

        public void Execute(IServiceProvider serviceProvider)
        {
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            var service = serviceFactory.CreateOrganizationService(context.UserId);
            var trace = (ITracingService)serviceProvider.GetService(typeof(ITracingService));

            try
            {
                trace.Trace("ClaimWeather started");

                if (!ValidateContext(context, trace))
                {
                    return;
                }

                Entity target = (Entity)context.InputParameters["Target"];

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

                Entity claim = service.Retrieve(ClaimEntityName, target.Id, new ColumnSet(
                    IncidentLatitudeField,
                    IncidentLongitudeField,
                    IncidentDateField,
                    WeatherConditionsField
                ));

                decimal? latitude = claim.GetAttributeValue<decimal?>(IncidentLatitudeField);
                decimal? longitude = claim.GetAttributeValue<decimal?>(IncidentLongitudeField);
                DateTime? incidentDate = claim.GetAttributeValue<DateTime?>(IncidentDateField);

                if (!latitude.HasValue || !longitude.HasValue)
                {
                    trace.Trace("ClaimWeather: Missing coordinates, skipping weather lookup");
                    return;
                }

                if (latitude.Value < -90m || latitude.Value > 90m ||
                    longitude.Value < -180m || longitude.Value > 180m)
                {
                    trace.Trace("ClaimWeather: Invalid coordinate range");
                    return;
                }

                if (!incidentDate.HasValue)
                {
                    trace.Trace("ClaimWeather: Missing incident date");
                    return;
                }

                // Weather API uses UTC; late-evening local times may return previous day's data
                if (incidentDate.Value.Date > DateTime.UtcNow.Date)
                {
                    trace.Trace("ClaimWeather: Incident date is in the future, skipping weather lookup");
                    return;
                }

                string apiUrl = GetEnvironmentVariable(service, EnvVarWeatherUrl, trace);
                string apiKey = GetEnvironmentVariable(service, EnvVarWeatherKey, trace);

                if (string.IsNullOrEmpty(apiUrl) || string.IsNullOrEmpty(apiKey))
                {
                    trace.Trace("ClaimWeather: Skipped - Environment variables {0} and {1} must be configured", EnvVarWeatherUrl, EnvVarWeatherKey);
                    return;
                }

                var weatherResult = CallWeatherApi(apiUrl, apiKey, latitude.Value, longitude.Value, incidentDate.Value, trace);

                if (weatherResult != null && weatherResult.Success && !string.IsNullOrEmpty(weatherResult.Conditions))
                {
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
                trace.Trace("ClaimWeather: Error - {0}", ex.Message);
            }
        }

        private bool ValidateContext(IPluginExecutionContext context, ITracingService trace)
        {
            if (!context.PrimaryEntityName.Equals(ClaimEntityName, StringComparison.OrdinalIgnoreCase))
            {
                trace.Trace("ClaimWeather: Wrong entity - {0}", context.PrimaryEntityName);
                return false;
            }

            string message = context.MessageName;
            if (!message.Equals("Create", StringComparison.OrdinalIgnoreCase) &&
                !message.Equals("Update", StringComparison.OrdinalIgnoreCase))
            {
                trace.Trace("ClaimWeather: Wrong message - {0}", message);
                return false;
            }

            if (!context.InputParameters.Contains("Target") || !(context.InputParameters["Target"] is Entity))
            {
                trace.Trace("ClaimWeather: No target entity");
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

        // HttpWebRequest used due to .NET 4.6.2 plugin runtime; revisit on upgrade
        private WeatherApiResponse CallWeatherApi(string apiUrl, string apiKey, decimal latitude, decimal longitude, DateTime date, ITracingService trace)
        {
            Exception lastException = null;

            for (int attempt = 1; attempt <= MaxRetries; attempt++)
            {
                try
                {
                    trace.Trace("Calling WeatherLookup function, attempt {0}/{1}", attempt, MaxRetries);

                    var request = (HttpWebRequest)WebRequest.Create(apiUrl);
                    request.Method = "POST";
                    request.ContentType = "application/json";
                    request.Timeout = ApiTimeoutMs;
                    request.Headers.Add("x-functions-key", apiKey);

                    var requestBody = new WeatherApiRequest
                    {
                        Latitude = latitude,
                        Longitude = longitude,
                        Date = date.ToString("yyyy-MM-dd")
                    };
                    byte[] bodyBytes = SerializeToJson(requestBody);
                    request.ContentLength = bodyBytes.Length;

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
                    lastException = webEx;
                    trace.Trace("Web error (attempt {0}): {1}", attempt, webEx.Message);

                    if (webEx.Response != null)
                    {
                        try
                        {
                            using (Stream errorStream = webEx.Response.GetResponseStream())
                            {
                                var errorResponse = DeserializeFromJson<WeatherApiResponse>(errorStream);
                                if (errorResponse != null) return errorResponse;
                            }
                        }
                        catch { }
                    }

                    if (!IsTransientError(webEx)) break;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    trace.Trace("API error (attempt {0}): {1}", attempt, ex.Message);
                }

                if (attempt < MaxRetries)
                {
                    int delayMs = BaseRetryDelayMs * (int)Math.Pow(2, attempt - 1);
                    trace.Trace("Retrying in {0}ms", delayMs);
                    System.Threading.Thread.Sleep(delayMs);
                }
            }

            trace.Trace("All retry attempts exhausted: {0}", lastException?.Message ?? "Unknown");
            return null;
        }

        private bool IsTransientError(WebException webEx)
        {
            if (webEx.Status == WebExceptionStatus.Timeout ||
                webEx.Status == WebExceptionStatus.ConnectFailure ||
                webEx.Status == WebExceptionStatus.NameResolutionFailure)
                return true;

            if (webEx.Response is HttpWebResponse httpResponse)
            {
                int statusCode = (int)httpResponse.StatusCode;
                return statusCode >= 500 && statusCode < 600;
            }

            return false;
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
