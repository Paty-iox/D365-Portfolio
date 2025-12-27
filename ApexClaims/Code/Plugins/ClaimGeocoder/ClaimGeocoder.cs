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
    /// <summary>
    /// Plugin to automatically geocode the incident location on a Claim record.
    /// Calls the GeocodeLocation Azure Function and updates latitude/longitude fields.
    ///
    /// Registration:
    /// - Table: new_claim
    /// - Messages: Create, Update
    /// - Filtering Attributes: new_incidentlocation
    /// - Stage: Post-Operation
    /// - Execution Mode: Synchronous
    ///
    /// Environment Variables Required:
    /// - icp_GeocodeApiUrl: Azure Function URL
    /// - icp_GeocodeApiKey: Azure Function key
    /// </summary>
    public class ClaimGeocoder : IPlugin
    {
        // Fallback values for development (Environment Variables override these)
        private const string DefaultGeocodeApiUrl = "https://ApexClaims-func.azurewebsites.net/api/geocodelocation";
        private const string DefaultGeocodeApiKey = "YOUR_FUNCTION_KEY_HERE";
        private const int ApiTimeoutMs = 15000;

        // Environment Variable schema names
        private const string EnvVarGeocodeUrl = "new_geocodeapiurl";
        private const string EnvVarGeocodeKey = "new_geocodeapikey";

        // Field names
        private const string IncidentLocationField = "new_incidentlocation";
        private const string IncidentLatitudeField = "new_incidentlatitude";
        private const string IncidentLongitudeField = "new_incidentlongitude";
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
                trace.Trace("ClaimGeocoder: Plugin execution started");

                // Validate context
                if (!ValidateContext(context, trace))
                {
                    return;
                }

                // Get target entity
                Entity target = (Entity)context.InputParameters["Target"];

                // For Update, only proceed if incident location was changed
                if (context.MessageName.Equals("Update", StringComparison.OrdinalIgnoreCase))
                {
                    if (!target.Contains(IncidentLocationField))
                    {
                        trace.Trace("ClaimGeocoder: Incident location not changed, skipping");
                        return;
                    }
                }

                // Get the incident location value
                string location = target.GetAttributeValue<string>(IncidentLocationField);

                // If location is empty, clear the coordinates (intentional clear)
                if (string.IsNullOrWhiteSpace(location))
                {
                    trace.Trace("ClaimGeocoder: Incident location is empty, clearing coordinates");
                    ClearCoordinates(service, target.Id, trace);
                    return;
                }

                // Get API configuration from Environment Variables
                string apiUrl = GetEnvironmentVariable(service, EnvVarGeocodeUrl, trace) ?? DefaultGeocodeApiUrl;
                string apiKey = GetEnvironmentVariable(service, EnvVarGeocodeKey, trace) ?? DefaultGeocodeApiKey;

                trace.Trace("ClaimGeocoder: Geocoding location (length: {0})", location.Length);

                // Call the geocode API
                GeocodeApiResponse geocodeResult = CallGeocodeApi(apiUrl, apiKey, location, trace);

                if (geocodeResult == null)
                {
                    // Transient error - preserve existing coordinates
                    trace.Trace("ClaimGeocoder: API call failed (transient), preserving existing coordinates");
                    return;
                }

                if (geocodeResult.Success && geocodeResult.Latitude.HasValue && geocodeResult.Longitude.HasValue)
                {
                    // Update the claim with coordinates
                    UpdateCoordinates(service, target.Id, geocodeResult.Latitude.Value, geocodeResult.Longitude.Value, trace);
                    trace.Trace("ClaimGeocoder: Successfully updated coordinates");
                }
                else if (geocodeResult.Error != null && geocodeResult.Error.Contains("not found"))
                {
                    // Address explicitly not found - clear coordinates
                    trace.Trace("ClaimGeocoder: Address not found, clearing coordinates");
                    ClearCoordinates(service, target.Id, trace);
                }
                else
                {
                    // Other API error - preserve existing coordinates
                    trace.Trace("ClaimGeocoder: API returned error, preserving existing coordinates - {0}",
                        geocodeResult.Error ?? "Unknown");
                }
            }
            catch (Exception ex)
            {
                // Log the error but don't throw - we don't want to block the user from saving
                trace.Trace("ClaimGeocoder: Error - {0}", ex.Message);
            }
        }

        private bool ValidateContext(IPluginExecutionContext context, ITracingService trace)
        {
            // Check entity name
            if (!context.PrimaryEntityName.Equals(ClaimEntityName, StringComparison.OrdinalIgnoreCase))
            {
                trace.Trace("ClaimGeocoder: Wrong entity - {0}", context.PrimaryEntityName);
                return false;
            }

            // Check message
            string message = context.MessageName;
            if (!message.Equals("Create", StringComparison.OrdinalIgnoreCase) &&
                !message.Equals("Update", StringComparison.OrdinalIgnoreCase))
            {
                trace.Trace("ClaimGeocoder: Wrong message - {0}", message);
                return false;
            }

            // Check for target entity
            if (!context.InputParameters.Contains("Target") || !(context.InputParameters["Target"] is Entity))
            {
                trace.Trace("ClaimGeocoder: No target entity found");
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
                trace.Trace("ClaimGeocoder: Failed to get environment variable {0} - {1}", schemaName, ex.Message);
            }

            return null;
        }

        private GeocodeApiResponse CallGeocodeApi(string apiUrl, string apiKey, string address, ITracingService trace)
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
                var requestBody = new GeocodeApiRequest { Address = address };
                byte[] bodyBytes = SerializeToJson(requestBody);
                request.ContentLength = bodyBytes.Length;

                trace.Trace("ClaimGeocoder: Sending request to geocode API");

                using (Stream requestStream = request.GetRequestStream())
                {
                    requestStream.Write(bodyBytes, 0, bodyBytes.Length);
                }

                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (Stream responseStream = response.GetResponseStream())
                {
                    trace.Trace("ClaimGeocoder: Response received - Status: {0}", response.StatusCode);
                    return DeserializeFromJson<GeocodeApiResponse>(responseStream);
                }
            }
            catch (WebException webEx)
            {
                trace.Trace("ClaimGeocoder: Web error - {0}", webEx.Message);

                // Try to parse error response
                if (webEx.Response != null)
                {
                    try
                    {
                        using (Stream errorStream = webEx.Response.GetResponseStream())
                        {
                            var errorResponse = DeserializeFromJson<GeocodeApiResponse>(errorStream);
                            if (errorResponse != null)
                            {
                                return errorResponse;
                            }
                        }
                    }
                    catch (Exception parseEx)
                    {
                        trace.Trace("ClaimGeocoder: Failed to parse error response - {0}", parseEx.Message);
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                trace.Trace("ClaimGeocoder: API call error - {0}", ex.Message);
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

        private void UpdateCoordinates(IOrganizationService service, Guid claimId, decimal latitude, decimal longitude, ITracingService trace)
        {
            try
            {
                Entity updateEntity = new Entity(ClaimEntityName, claimId);
                updateEntity[IncidentLatitudeField] = latitude;
                updateEntity[IncidentLongitudeField] = longitude;
                service.Update(updateEntity);

                trace.Trace("ClaimGeocoder: Coordinates updated successfully");
            }
            catch (Exception ex)
            {
                trace.Trace("ClaimGeocoder: Failed to update coordinates - {0}", ex.Message);
            }
        }

        private void ClearCoordinates(IOrganizationService service, Guid claimId, ITracingService trace)
        {
            try
            {
                Entity updateEntity = new Entity(ClaimEntityName, claimId);
                updateEntity[IncidentLatitudeField] = null;
                updateEntity[IncidentLongitudeField] = null;
                service.Update(updateEntity);

                trace.Trace("ClaimGeocoder: Coordinates cleared");
            }
            catch (Exception ex)
            {
                trace.Trace("ClaimGeocoder: Failed to clear coordinates - {0}", ex.Message);
            }
        }
    }

    #region Data Contracts

    [DataContract]
    internal class GeocodeApiRequest
    {
        [DataMember(Name = "address")]
        public string Address { get; set; }
    }

    [DataContract]
    internal class GeocodeApiResponse
    {
        [DataMember(Name = "success")]
        public bool Success { get; set; }

        [DataMember(Name = "latitude")]
        public decimal? Latitude { get; set; }

        [DataMember(Name = "longitude")]
        public decimal? Longitude { get; set; }

        [DataMember(Name = "formattedAddress")]
        public string FormattedAddress { get; set; }

        [DataMember(Name = "confidence")]
        public string Confidence { get; set; }

        [DataMember(Name = "error")]
        public string Error { get; set; }
    }

    #endregion
}
