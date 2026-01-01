using System;
using System.IO;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace ApexClaims.Plugins
{
    // Geocodes incident location on Create/Update of new_claim
    // Register: Post-Operation, Asynchronous, filter on new_incidentlocation
    // Note: Async avoids blocking user saves during external API calls
    public class ClaimGeocoder : IPlugin
    {
        private const string DefaultGeocodeApiUrl = "https://ApexClaims-func.azurewebsites.net/api/geocodelocation";
        private const string DefaultGeocodeApiKey = "YOUR_FUNCTION_KEY_HERE";
        private const int ApiTimeoutMs = 15000;

        private const string EnvVarGeocodeUrl = "new_geocodeapiurl";
        private const string EnvVarGeocodeKey = "new_geocodeapikey";

        private const string IncidentLocationField = "new_incidentlocation";
        private const string IncidentLatitudeField = "new_incidentlatitude";
        private const string IncidentLongitudeField = "new_incidentlongitude";
        private const string ClaimEntityName = "new_claim";

        public void Execute(IServiceProvider serviceProvider)
        {
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            var service = serviceFactory.CreateOrganizationService(context.UserId);
            var trace = (ITracingService)serviceProvider.GetService(typeof(ITracingService));

            try
            {
                trace.Trace("ClaimGeocoder started");

                if (!ValidateContext(context, trace))
                    return;

                Entity target = (Entity)context.InputParameters["Target"];

                if (context.MessageName.Equals("Update", StringComparison.OrdinalIgnoreCase) && !target.Contains(IncidentLocationField))
                {
                    trace.Trace("Location not changed, skipping");
                    return;
                }

                string location = target.GetAttributeValue<string>(IncidentLocationField);

                if (string.IsNullOrWhiteSpace(location))
                {
                    trace.Trace("Location empty, clearing coordinates");
                    ClearCoordinates(service, target.Id, trace);
                    return;
                }

                string apiUrl = GetEnvironmentVariable(service, EnvVarGeocodeUrl, trace) ?? DefaultGeocodeApiUrl;
                string apiKey = GetEnvironmentVariable(service, EnvVarGeocodeKey, trace) ?? DefaultGeocodeApiKey;

                trace.Trace("Geocoding location");
                var geocodeResult = CallGeocodeApi(apiUrl, apiKey, location, trace);

                if (geocodeResult == null)
                {
                    trace.Trace("API failed, preserving existing coordinates");
                    return;
                }

                if (geocodeResult.Success && geocodeResult.Latitude.HasValue && geocodeResult.Longitude.HasValue)
                {
                    UpdateCoordinates(service, target.Id, geocodeResult.Latitude.Value, geocodeResult.Longitude.Value, trace);
                    trace.Trace("Coordinates updated");
                }
                else if (geocodeResult.Error != null && geocodeResult.Error.Contains("not found"))
                {
                    trace.Trace("Address not found, clearing coordinates");
                    ClearCoordinates(service, target.Id, trace);
                }
                else
                {
                    trace.Trace("API error: {0}", geocodeResult.Error ?? "Unknown");
                }
            }
            catch (Exception ex)
            {
                trace.Trace("Error: {0}", ex.Message);
            }
        }

        private bool ValidateContext(IPluginExecutionContext context, ITracingService trace)
        {
            if (!context.PrimaryEntityName.Equals(ClaimEntityName, StringComparison.OrdinalIgnoreCase))
            {
                trace.Trace("Wrong entity: {0}", context.PrimaryEntityName);
                return false;
            }

            string message = context.MessageName;
            if (!message.Equals("Create", StringComparison.OrdinalIgnoreCase) && !message.Equals("Update", StringComparison.OrdinalIgnoreCase))
            {
                trace.Trace("Wrong message: {0}", message);
                return false;
            }

            if (!context.InputParameters.Contains("Target") || !(context.InputParameters["Target"] is Entity))
            {
                trace.Trace("No target entity");
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
                    Criteria = new FilterExpression { Conditions = { new ConditionExpression("statecode", ConditionOperator.Equal, 0) } },
                    LinkEntities = {
                        new LinkEntity {
                            LinkFromEntityName = "environmentvariablevalue",
                            LinkToEntityName = "environmentvariabledefinition",
                            LinkFromAttributeName = "environmentvariabledefinitionid",
                            LinkToAttributeName = "environmentvariabledefinitionid",
                            LinkCriteria = new FilterExpression { Conditions = { new ConditionExpression("schemaname", ConditionOperator.Equal, schemaName) } }
                        }
                    }
                };

                var results = service.RetrieveMultiple(query);
                if (results.Entities.Count > 0)
                    return results.Entities[0].GetAttributeValue<string>("value");
            }
            catch (Exception ex)
            {
                trace.Trace("Failed to get env var {0}: {1}", schemaName, ex.Message);
            }
            return null;
        }

        private GeocodeApiResponse CallGeocodeApi(string apiUrl, string apiKey, string address, ITracingService trace)
        {
            try
            {
                var request = (HttpWebRequest)WebRequest.Create(apiUrl);
                request.Method = "POST";
                request.ContentType = "application/json";
                request.Timeout = ApiTimeoutMs;
                request.Headers.Add("x-functions-key", apiKey);

                var requestBody = new GeocodeApiRequest { Address = address };
                byte[] bodyBytes = SerializeToJson(requestBody);
                request.ContentLength = bodyBytes.Length;

                using (Stream requestStream = request.GetRequestStream())
                    requestStream.Write(bodyBytes, 0, bodyBytes.Length);

                using (var response = (HttpWebResponse)request.GetResponse())
                using (Stream responseStream = response.GetResponseStream())
                {
                    trace.Trace("Response: {0}", response.StatusCode);
                    return DeserializeFromJson<GeocodeApiResponse>(responseStream);
                }
            }
            catch (WebException webEx)
            {
                trace.Trace("Web error: {0}", webEx.Message);
                if (webEx.Response != null)
                {
                    try
                    {
                        using (Stream errorStream = webEx.Response.GetResponseStream())
                            return DeserializeFromJson<GeocodeApiResponse>(errorStream);
                    }
                    catch { }
                }
                return null;
            }
            catch (Exception ex)
            {
                trace.Trace("API error: {0}", ex.Message);
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
                var updateEntity = new Entity(ClaimEntityName, claimId);
                updateEntity[IncidentLatitudeField] = latitude;
                updateEntity[IncidentLongitudeField] = longitude;
                service.Update(updateEntity);
            }
            catch (Exception ex)
            {
                trace.Trace("Failed to update coordinates: {0}", ex.Message);
            }
        }

        private void ClearCoordinates(IOrganizationService service, Guid claimId, ITracingService trace)
        {
            try
            {
                var updateEntity = new Entity(ClaimEntityName, claimId);
                updateEntity[IncidentLatitudeField] = null;
                updateEntity[IncidentLongitudeField] = null;
                service.Update(updateEntity);
            }
            catch (Exception ex)
            {
                trace.Trace("Failed to clear coordinates: {0}", ex.Message);
            }
        }
    }

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
}
