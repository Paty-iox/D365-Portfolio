using System;
using System.Collections.Generic;
using Contoso.VendorRisk.Plugins.Models;
using Contoso.VendorRisk.Plugins.Services;
using Microsoft.Xrm.Sdk;
using Newtonsoft.Json;

namespace Contoso.VendorRisk.Plugins.Plugins
{
    public class BulkComplianceCheckPlugin : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            var tracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            var service = serviceFactory.CreateOrganizationService(context.UserId);

            tracingService.Trace("BulkComplianceCheckPlugin started");

            try
            {
                var input = new BulkComplianceInput
                {
                    VendorIds = ParseVendorIds(context, tracingService),
                    CheckTypes = ParseCheckTypes(context, tracingService),
                    FailFast = GetFailFast(context)
                };

                tracingService.Trace("Input - VendorCount: {0}, CheckTypes: {1}, FailFast: {2}",
                    input.VendorIds.Count,
                    input.CheckTypes.Count > 0 ? string.Join(", ", input.CheckTypes) : "All",
                    input.FailFast);

                var bulkService = new BulkComplianceService(service, tracingService);
                var result = bulkService.ExecuteBulkCheck(input);

                // Core result counts
                context.OutputParameters["TotalVendors"] = result.TotalVendors;
                context.OutputParameters["PassedCount"] = result.PassedCount;
                context.OutputParameters["FailedCount"] = result.FailedCount;
                context.OutputParameters["Results"] = JsonConvert.SerializeObject(result.Results);
                context.OutputParameters["ExecutionTimeMs"] = (int)result.ExecutionTimeMs;

                // FailFast and processing diagnostics
                context.OutputParameters["RequestedCount"] = result.RequestedCount;
                context.OutputParameters["ProcessedCount"] = result.ProcessedCount;
                context.OutputParameters["FailFastTriggered"] = result.FailFastTriggered;
                context.OutputParameters["SkippedVendorIds"] = JsonConvert.SerializeObject(result.SkippedVendorIds);

                // Configuration observability
                context.OutputParameters["ConfigSource"] = result.ConfigSource ?? string.Empty;
                context.OutputParameters["ConfigWarning"] = result.ConfigWarning ?? string.Empty;

                tracingService.Trace("BulkComplianceCheckPlugin completed - Requested: {0}, Processed: {1}, FailFast: {2}, ConfigSource: {3}",
                    result.RequestedCount, result.ProcessedCount, result.FailFastTriggered, result.ConfigSource);
            }
            catch (InvalidPluginExecutionException)
            {
                throw;
            }
            catch (Exception ex)
            {
                tracingService.Trace("Error: {0}\n{1}", ex.Message, ex.StackTrace);
                throw new InvalidPluginExecutionException($"Error executing bulk compliance check: {ex.Message}", ex);
            }
        }

        private List<Guid> ParseVendorIds(IPluginExecutionContext context, ITracingService tracingService)
        {
            if (!context.InputParameters.Contains("VendorIds"))
                throw new InvalidPluginExecutionException("VendorIds is required.");

            var value = context.InputParameters["VendorIds"];
            if (value == null || (value is string s && string.IsNullOrWhiteSpace(s)))
                throw new InvalidPluginExecutionException("VendorIds cannot be null or empty.");

            var vendorIdsJson = value as string;
            if (vendorIdsJson == null)
                throw new InvalidPluginExecutionException($"VendorIds must be a JSON string. Received type: {value.GetType().Name}");

            try
            {
                var guidStrings = JsonConvert.DeserializeObject<List<string>>(vendorIdsJson);
                if (guidStrings == null || guidStrings.Count == 0)
                    throw new InvalidPluginExecutionException("VendorIds array cannot be empty.");

                var guids = new List<Guid>();
                foreach (var guidString in guidStrings)
                {
                    if (Guid.TryParse(guidString, out var guid))
                    {
                        guids.Add(guid);
                    }
                    else
                    {
                        throw new InvalidPluginExecutionException($"Invalid GUID format in VendorIds: '{guidString}'");
                    }
                }

                tracingService.Trace("Parsed {0} vendor GUIDs", guids.Count);
                return guids;
            }
            catch (JsonException ex)
            {
                throw new InvalidPluginExecutionException($"VendorIds is not valid JSON: {ex.Message}");
            }
        }

        private List<string> ParseCheckTypes(IPluginExecutionContext context, ITracingService tracingService)
        {
            if (!context.InputParameters.Contains("CheckTypes"))
                return new List<string>();

            var value = context.InputParameters["CheckTypes"];
            if (value == null)
                return new List<string>();

            var checkTypesJson = value as string;
            if (string.IsNullOrWhiteSpace(checkTypesJson))
                return new List<string>();

            try
            {
                var checkTypes = JsonConvert.DeserializeObject<List<string>>(checkTypesJson);
                if (checkTypes == null)
                    return new List<string>();

                // Validate check types
                var validTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    CheckType.DocumentExpiry,
                    CheckType.SAMStatus,
                    CheckType.Debarment,
                    CheckType.OSHAViolations,
                    CheckType.ERPStatus
                };

                var invalidTypes = new List<string>();
                foreach (var ct in checkTypes)
                {
                    if (!validTypes.Contains(ct))
                        invalidTypes.Add(ct);
                }

                if (invalidTypes.Count > 0)
                {
                    tracingService.Trace("Warning: Invalid check types will be ignored: {0}", string.Join(", ", invalidTypes));
                }

                // Return normalized check types (matching case from constants)
                var normalizedTypes = new List<string>();
                foreach (var ct in checkTypes)
                {
                    foreach (var valid in CheckType.All)
                    {
                        if (string.Equals(ct, valid, StringComparison.OrdinalIgnoreCase))
                        {
                            normalizedTypes.Add(valid);
                            break;
                        }
                    }
                }

                tracingService.Trace("Parsed check types: {0}", string.Join(", ", normalizedTypes));
                return normalizedTypes;
            }
            catch (JsonException ex)
            {
                tracingService.Trace("Warning: CheckTypes is not valid JSON ({0}) - using all checks", ex.Message);
                return new List<string>();
            }
        }

        private bool GetFailFast(IPluginExecutionContext context)
        {
            if (!context.InputParameters.Contains("FailFast"))
                return false;

            var value = context.InputParameters["FailFast"];
            if (value == null)
                return false;

            if (value is bool boolValue)
                return boolValue;

            if (value is string stringValue)
                return string.Equals(stringValue, "true", StringComparison.OrdinalIgnoreCase);

            return false;
        }
    }
}
