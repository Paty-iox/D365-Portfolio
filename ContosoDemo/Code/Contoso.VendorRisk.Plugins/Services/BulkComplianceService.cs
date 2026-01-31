using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.ServiceModel;
using Contoso.VendorRisk.Plugins.Models;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Newtonsoft.Json;

namespace Contoso.VendorRisk.Plugins.Services
{
    public class BulkComplianceService
    {
        private readonly IOrganizationService _service;
        private readonly ITracingService _tracingService;
        private readonly BulkComplianceConfig _config;
        private readonly string _configSource;
        private readonly string _configWarning;

        private const int BULK_COMPLIANCE_CONFIG_TYPE = 100000001;

        public BulkComplianceService(IOrganizationService service, ITracingService tracingService)
        {
            _service = service;
            _tracingService = tracingService;
            _config = LoadConfig(out _configSource, out _configWarning);
        }

        public string ConfigSource => _configSource;
        public string ConfigWarning => _configWarning;

        private BulkComplianceConfig LoadConfig(out string configSource, out string configWarning)
        {
            configWarning = null;

            try
            {
                var query = new QueryExpression("contoso_apiconfig")
                {
                    ColumnSet = new ColumnSet("contoso_configjson"),
                    Criteria = new FilterExpression
                    {
                        Conditions =
                        {
                            new ConditionExpression("contoso_configtype", ConditionOperator.Equal, BULK_COMPLIANCE_CONFIG_TYPE),
                            new ConditionExpression("contoso_isactive", ConditionOperator.Equal, true),
                            new ConditionExpression("statecode", ConditionOperator.Equal, 0)
                        }
                    },
                    TopCount = 1
                };

                var results = _service.RetrieveMultiple(query);
                if (results.Entities.Count == 0)
                {
                    _tracingService.Trace("WARNING: No active Bulk Compliance config found - using defaults. Check behavior may differ from expected.");
                    configSource = ConfigurationSource.DefaultsDueToMissing;
                    configWarning = "No active Bulk Compliance configuration found in contoso_apiconfig. Using default values.";
                    return BulkComplianceConfig.GetDefaults();
                }

                var configJson = results.Entities[0].GetAttributeValue<string>("contoso_configjson");
                if (string.IsNullOrEmpty(configJson))
                {
                    _tracingService.Trace("WARNING: Config JSON is empty - using defaults. Check behavior may differ from expected.");
                    configSource = ConfigurationSource.DefaultsDueToEmpty;
                    configWarning = "Bulk Compliance configuration record exists but JSON is empty. Using default values.";
                    return BulkComplianceConfig.GetDefaults();
                }

                var config = JsonConvert.DeserializeObject<BulkComplianceConfig>(configJson);
                config.MergeWithDefaults();

                _tracingService.Trace("Loaded Bulk Compliance config from Dataverse");
                configSource = ConfigurationSource.Database;
                return config;
            }
            catch (Exception ex)
            {
                _tracingService.Trace("ERROR loading config: {0} - using defaults. Check behavior may differ from expected.", ex.Message);
                configSource = ConfigurationSource.DefaultsDueToError;
                configWarning = $"Error loading Bulk Compliance configuration: {ex.Message}. Using default values.";
                return BulkComplianceConfig.GetDefaults();
            }
        }

        public BulkComplianceResult ExecuteBulkCheck(BulkComplianceInput input)
        {
            var stopwatch = Stopwatch.StartNew();
            var result = new BulkComplianceResult();

            // Track requested count upfront (before any processing)
            result.RequestedCount = input.VendorIds.Count;

            _tracingService.Trace("Starting BulkComplianceCheck for {0} vendors (OPTIMIZED)", input.VendorIds.Count);

            // Validate vendor count
            if (input.VendorIds.Count > _config.MaxVendorsPerCall)
            {
                throw new InvalidPluginExecutionException(
                    $"Vendor count ({input.VendorIds.Count}) exceeds maximum allowed ({_config.MaxVendorsPerCall}).");
            }

            // Determine which checks to run
            var checksToRun = GetChecksToRun(input.CheckTypes);
            _tracingService.Trace("Checks to run: {0}", string.Join(", ", checksToRun));

            // OPTIMIZATION: Batch load all data upfront
            _tracingService.Trace("Loading all vendor data in batch...");
            var sw = Stopwatch.StartNew();
            var allVendorData = GetAllVendorData(input.VendorIds);
            _tracingService.Trace("Vendor data loaded in {0}ms", sw.ElapsedMilliseconds);

            // Get all vendor numbers for external lookups
            var vendorNumbers = allVendorData.Values
                .Where(v => !string.IsNullOrEmpty(v.VendorNumber))
                .Select(v => v.VendorNumber)
                .ToList();

            sw.Restart();
            var allComplianceData = GetAllComplianceData(input.VendorIds);
            _tracingService.Trace("Compliance data loaded in {0}ms", sw.ElapsedMilliseconds);

            sw.Restart();
            var allErpData = GetAllERPData(vendorNumbers);
            _tracingService.Trace("ERP data loaded in {0}ms ({1} records)", sw.ElapsedMilliseconds, allErpData.Count);

            sw.Restart();
            var allRegistryData = GetAllRegistryData(vendorNumbers, out bool registryFetchFailed);
            _tracingService.Trace("Registry data loaded in {0}ms ({1} records, fetchFailed={2})", sw.ElapsedMilliseconds, allRegistryData.Count, registryFetchFailed);

            // Process each vendor using pre-loaded data
            var failFastTriggered = false;
            var processedIndex = 0;

            foreach (var vendorId in input.VendorIds)
            {
                // If FailFast already triggered, track skipped vendors
                if (failFastTriggered)
                {
                    result.SkippedVendorIds.Add(vendorId);
                    continue;
                }

                _tracingService.Trace("Processing vendor: {0}", vendorId);
                processedIndex++;

                var vendorResult = ProcessVendorWithCachedData(
                    vendorId,
                    checksToRun,
                    allVendorData,
                    allComplianceData,
                    allErpData,
                    allRegistryData,
                    registryFetchFailed);

                result.Results.Add(vendorResult);

                // Update counts
                if (vendorResult.OverallStatus == CheckStatus.Pass)
                    result.PassedCount++;
                else if (vendorResult.OverallStatus == CheckStatus.Fail)
                    result.FailedCount++;

                // FailFast logic - set flag but continue loop to track skipped vendors
                if (input.FailFast && vendorResult.OverallStatus == CheckStatus.Fail)
                {
                    _tracingService.Trace("FailFast triggered at vendor {0} - remaining vendors will be skipped", vendorId);
                    failFastTriggered = true;
                    result.FailFastTriggered = true;
                }
            }

            result.ProcessedCount = result.Results.Count;
            result.TotalVendors = result.RequestedCount; // TotalVendors now reflects what was requested
            stopwatch.Stop();
            result.ExecutionTimeMs = stopwatch.ElapsedMilliseconds;

            // Include configuration observability info
            result.ConfigSource = _configSource;
            result.ConfigWarning = _configWarning;

            _tracingService.Trace("BulkComplianceCheck completed - Requested: {0}, Processed: {1}, Skipped: {2}, Passed: {3}, Failed: {4}, FailFast: {5}, Time: {6}ms",
                result.RequestedCount, result.ProcessedCount, result.SkippedVendorIds.Count,
                result.PassedCount, result.FailedCount, result.FailFastTriggered, result.ExecutionTimeMs);

            return result;
        }

        private List<string> GetChecksToRun(List<string> requestedChecks)
        {
            if (requestedChecks == null || requestedChecks.Count == 0)
            {
                return _config.EnabledCheckTypes;
            }

            // Return normalized check type names from EnabledCheckTypes (not original casing)
            // to ensure case-sensitive switch in RunCheck matches correctly
            return _config.EnabledCheckTypes
                .Where(e => requestedChecks.Any(c => string.Equals(e, c, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        private VendorComplianceResult ProcessVendorWithCachedData(
            Guid vendorId,
            List<string> checksToRun,
            Dictionary<Guid, VendorData> vendorDataCache,
            Dictionary<Guid, ComplianceData> complianceDataCache,
            Dictionary<string, ERPVendorData> erpDataCache,
            Dictionary<string, ComplianceRegistryData> registryDataCache,
            bool registryFetchFailed)
        {
            var result = new VendorComplianceResult
            {
                VendorId = vendorId
            };

            try
            {
                // Get vendor data from cache
                if (!vendorDataCache.TryGetValue(vendorId, out var vendorData))
                {
                    result.OverallStatus = CheckStatus.Fail;
                    result.ErrorMessage = "Vendor not found";
                    result.FailedChecks = checksToRun.Count;
                    return result;
                }

                result.VendorNumber = vendorData.VendorNumber;
                result.VendorName = vendorData.VendorName;

                // Get compliance data from cache
                complianceDataCache.TryGetValue(vendorId, out var complianceData);
                if (complianceData == null)
                    complianceData = new ComplianceData();

                // Get external data from cache
                ERPVendorData erpData = null;
                ComplianceRegistryData registryData = null;

                if (!string.IsNullOrEmpty(vendorData.VendorNumber))
                {
                    erpDataCache.TryGetValue(vendorData.VendorNumber, out erpData);
                    registryDataCache.TryGetValue(vendorData.VendorNumber, out registryData);
                }

                // Run each check
                foreach (var checkType in checksToRun)
                {
                    var check = RunCheck(checkType, complianceData, erpData, registryData, registryFetchFailed);
                    result.Checks.Add(check);

                    if (check.Status == CheckStatus.Fail)
                        result.FailedChecks++;
                    else if (check.Status == CheckStatus.Warning)
                        result.WarningChecks++;
                }

                result.OverallStatus = DetermineOverallStatus(result.Checks);
            }
            catch (Exception ex)
            {
                _tracingService.Trace("Error processing vendor {0}: {1}", vendorId, ex.Message);
                result.OverallStatus = CheckStatus.Fail;
                result.ErrorMessage = $"Processing error: {ex.Message}";
                result.FailedChecks = checksToRun.Count;
            }

            return result;
        }

        #region Batch Data Retrieval Methods

        private Dictionary<Guid, VendorData> GetAllVendorData(List<Guid> vendorIds)
        {
            var result = new Dictionary<Guid, VendorData>();

            try
            {
                var query = new QueryExpression("contoso_vendor")
                {
                    ColumnSet = new ColumnSet("contoso_vendorid", "contoso_vendornumber", "contoso_vendorname"),
                    Criteria = new FilterExpression
                    {
                        Conditions =
                        {
                            new ConditionExpression("contoso_vendorid", ConditionOperator.In, vendorIds.Cast<object>().ToArray())
                        }
                    }
                };

                var entities = _service.RetrieveMultiple(query);

                foreach (var entity in entities.Entities)
                {
                    var vendorId = entity.Id;
                    result[vendorId] = new VendorData
                    {
                        VendorId = vendorId,
                        VendorNumber = entity.GetAttributeValue<string>("contoso_vendornumber"),
                        VendorName = entity.GetAttributeValue<string>("contoso_vendorname")
                    };
                }
            }
            catch (Exception ex)
            {
                _tracingService.Trace("Error batch loading vendors: {0}", ex.Message);
            }

            return result;
        }

        private Dictionary<Guid, ComplianceData> GetAllComplianceData(List<Guid> vendorIds)
        {
            var result = new Dictionary<Guid, ComplianceData>();
            var today = DateTime.UtcNow.Date;
            var warningDate = today.AddDays(_config.DocumentExpiryWarningDays);

            try
            {
                var query = new QueryExpression("contoso_compliancerecord")
                {
                    ColumnSet = new ColumnSet("contoso_vendor", "contoso_expirationdate", "statecode"),
                    Criteria = new FilterExpression
                    {
                        Conditions =
                        {
                            new ConditionExpression("contoso_vendor", ConditionOperator.In, vendorIds.Cast<object>().ToArray())
                        }
                    },
                    PageInfo = new PagingInfo
                    {
                        Count = 5000,
                        PageNumber = 1,
                        ReturnTotalRecordCount = false
                    }
                };

                EntityCollection records;
                int totalRecordsProcessed = 0;

                do
                {
                    records = _service.RetrieveMultiple(query);
                    totalRecordsProcessed += records.Entities.Count;

                    foreach (var record in records.Entities)
                    {
                        var vendorRef = record.GetAttributeValue<EntityReference>("contoso_vendor");
                        if (vendorRef == null) continue;

                        var vendorId = vendorRef.Id;
                        if (!result.ContainsKey(vendorId))
                        {
                            result[vendorId] = new ComplianceData();
                        }

                        var data = result[vendorId];
                        data.TotalDocuments++;

                        var expirationDate = record.GetAttributeValue<DateTime?>("contoso_expirationdate");
                        var stateCode = record.GetAttributeValue<OptionSetValue>("statecode")?.Value ?? 0;

                        if (stateCode == 0) // Active document
                        {
                            if (expirationDate.HasValue)
                            {
                                if (expirationDate.Value >= today)
                                {
                                    data.CurrentDocuments++;
                                    if (expirationDate.Value <= warningDate)
                                    {
                                        data.ExpiringSoonDocuments++;
                                    }
                                }
                                else
                                {
                                    data.ExpiredDocuments++;
                                }
                            }
                            else
                            {
                                // Active document without expiration date - unknown compliance status
                                data.MissingExpirationDocuments++;
                            }
                        }
                        else if (stateCode == 1) // Inactive document
                        {
                            data.ExpiredDocuments++;
                        }
                    }

                    // Set up next page
                    if (records.MoreRecords)
                    {
                        query.PageInfo.PageNumber++;
                        query.PageInfo.PagingCookie = records.PagingCookie;
                    }

                } while (records.MoreRecords);

                if (totalRecordsProcessed > 5000)
                {
                    _tracingService.Trace("Paged through {0} compliance records across multiple pages", totalRecordsProcessed);
                }
            }
            catch (Exception ex)
            {
                _tracingService.Trace("Error batch loading compliance data: {0}", ex.Message);
            }

            return result;
        }

        private Dictionary<string, ERPVendorData> GetAllERPData(List<string> vendorNumbers)
        {
            var result = new Dictionary<string, ERPVendorData>(StringComparer.OrdinalIgnoreCase);

            if (vendorNumbers == null || vendorNumbers.Count == 0)
                return result;

            try
            {
                var query = new QueryExpression("contoso_erpvendormaster")
                {
                    ColumnSet = new ColumnSet("contoso_name", "contoso_erpstatus"),
                    Criteria = new FilterExpression
                    {
                        Conditions =
                        {
                            new ConditionExpression("contoso_name", ConditionOperator.In, vendorNumbers.Cast<object>().ToArray())
                        }
                    }
                };

                var entities = _service.RetrieveMultiple(query);

                foreach (var entity in entities.Entities)
                {
                    var vendorNumber = entity.GetAttributeValue<string>("contoso_name");
                    if (!string.IsNullOrEmpty(vendorNumber))
                    {
                        result[vendorNumber] = new ERPVendorData
                        {
                            VendorNumber = vendorNumber,
                            ERPStatus = entity.GetAttributeValue<string>("contoso_erpstatus")
                        };
                    }
                }
            }
            catch (FaultException<OrganizationServiceFault> ex)
            {
                _tracingService.Trace("ERP Virtual Table batch error: {0}", ex.Message);
            }
            catch (Exception ex)
            {
                _tracingService.Trace("Error batch loading ERP data: {0}", ex.Message);
            }

            return result;
        }

        private Dictionary<string, ComplianceRegistryData> GetAllRegistryData(List<string> vendorNumbers, out bool registryFetchFailed)
        {
            var result = new Dictionary<string, ComplianceRegistryData>(StringComparer.OrdinalIgnoreCase);
            registryFetchFailed = false;

            if (vendorNumbers == null || vendorNumbers.Count == 0)
                return result;

            try
            {
                var query = new QueryExpression("contoso_complianceregistry")
                {
                    ColumnSet = new ColumnSet(
                        "contoso_name",
                        "contoso_samstatus",
                        "contoso_samexpiry",
                        "contoso_oshaviolationcount",
                        "contoso_oshalastinspection",
                        "contoso_debarred"
                    ),
                    Criteria = new FilterExpression
                    {
                        Conditions =
                        {
                            new ConditionExpression("contoso_name", ConditionOperator.In, vendorNumbers.Cast<object>().ToArray())
                        }
                    }
                };

                var entities = _service.RetrieveMultiple(query);

                foreach (var entity in entities.Entities)
                {
                    var vendorNumber = entity.GetAttributeValue<string>("contoso_name");
                    if (!string.IsNullOrEmpty(vendorNumber))
                    {
                        result[vendorNumber] = new ComplianceRegistryData
                        {
                            VendorNumber = vendorNumber,
                            SAMStatus = entity.GetAttributeValue<string>("contoso_samstatus"),
                            SAMExpiry = entity.GetAttributeValue<DateTime?>("contoso_samexpiry"),
                            OSHAViolationCount = entity.GetAttributeValue<int?>("contoso_oshaviolationcount"),
                            OSHALastInspection = entity.GetAttributeValue<DateTime?>("contoso_oshalastinspection"),
                            Debarred = entity.GetAttributeValue<bool?>("contoso_debarred") ?? false
                        };
                    }
                }
            }
            catch (FaultException<OrganizationServiceFault> ex)
            {
                _tracingService.Trace("CRITICAL: Registry Virtual Table batch error - debarment verification will fail closed: {0}", ex.Message);
                registryFetchFailed = true;
            }
            catch (Exception ex)
            {
                _tracingService.Trace("CRITICAL: Error batch loading registry data - debarment verification will fail closed: {0}", ex.Message);
                registryFetchFailed = true;
            }

            return result;
        }

        #endregion

        #region Check Methods

        private ComplianceCheck RunCheck(string checkType, ComplianceData complianceData, ERPVendorData erpData, ComplianceRegistryData registryData, bool registryFetchFailed)
        {
            switch (checkType)
            {
                case CheckType.DocumentExpiry:
                    return DocumentExpiryCheck(complianceData);
                case CheckType.SAMStatus:
                    return SAMStatusCheck(registryData, registryFetchFailed);
                case CheckType.Debarment:
                    return DebarmentCheck(registryData, registryFetchFailed);
                case CheckType.OSHAViolations:
                    return OSHAViolationsCheck(registryData, registryFetchFailed);
                case CheckType.ERPStatus:
                    return ERPStatusCheck(erpData);
                default:
                    return new ComplianceCheck
                    {
                        CheckType = checkType,
                        Status = CheckStatus.Skipped,
                        Message = "Unknown check type"
                    };
            }
        }

        private ComplianceCheck DocumentExpiryCheck(ComplianceData data)
        {
            var check = new ComplianceCheck { CheckType = CheckType.DocumentExpiry };

            if (data == null || data.TotalDocuments == 0)
            {
                check.Status = CheckStatus.Warning;
                check.Message = "No compliance documents on file";
                check.Details["totalDocuments"] = 0;
                return check;
            }

            check.Details["totalDocuments"] = data.TotalDocuments;
            check.Details["currentDocuments"] = data.CurrentDocuments;
            check.Details["expiredDocuments"] = data.ExpiredDocuments;
            check.Details["expiringSoonDocuments"] = data.ExpiringSoonDocuments;
            check.Details["missingExpirationDocuments"] = data.MissingExpirationDocuments;

            if (data.ExpiredDocuments > 0)
            {
                check.Status = CheckStatus.Fail;
                check.Message = $"{data.ExpiredDocuments} expired document(s)";
            }
            else if (data.MissingExpirationDocuments > 0)
            {
                // Documents without expiration dates represent unknown compliance status - treat as warning
                check.Status = CheckStatus.Warning;
                check.Message = $"{data.MissingExpirationDocuments} document(s) missing expiration date - compliance status unknown";
            }
            else if (data.ExpiringSoonDocuments > 0)
            {
                check.Status = CheckStatus.Warning;
                check.Message = $"{data.ExpiringSoonDocuments} document(s) expiring within {_config.DocumentExpiryWarningDays} days";
            }
            else
            {
                check.Status = CheckStatus.Pass;
                check.Message = "All documents current";
            }

            return check;
        }

        private ComplianceCheck SAMStatusCheck(ComplianceRegistryData data, bool registryFetchFailed)
        {
            var check = new ComplianceCheck { CheckType = CheckType.SAMStatus };

            if (data == null)
            {
                // If registry fetch failed, fail closed to prevent bypassing SAM verification
                if (registryFetchFailed)
                {
                    check.Status = CheckStatus.Fail;
                    check.Message = "CRITICAL: Registry lookup failed - SAM verification cannot be bypassed";
                    check.Details["registryFetchFailed"] = true;
                    return check;
                }
                check.Status = CheckStatus.Warning;
                check.Message = "No SAM.gov data available";
                return check;
            }

            check.Details["samStatus"] = data.SAMStatus ?? "Unknown";
            check.Details["samExpiry"] = data.SAMExpiry?.ToString("yyyy-MM-dd") ?? "N/A";

            if (string.IsNullOrEmpty(data.SAMStatus))
            {
                check.Status = CheckStatus.Warning;
                check.Message = "SAM status not recorded";
                return check;
            }

            var isAllowed = _config.AllowedSAMStatuses
                .Any(s => string.Equals(s, data.SAMStatus, StringComparison.OrdinalIgnoreCase));

            if (isAllowed)
            {
                check.Status = CheckStatus.Pass;
                check.Message = $"SAM status: {data.SAMStatus}";
            }
            else if (string.Equals(data.SAMStatus, "Expired", StringComparison.OrdinalIgnoreCase))
            {
                check.Status = CheckStatus.Fail;
                check.Message = "SAM.gov registration expired";
            }
            else if (string.Equals(data.SAMStatus, "Inactive", StringComparison.OrdinalIgnoreCase))
            {
                check.Status = CheckStatus.Fail;
                check.Message = "SAM.gov registration inactive";
            }
            else
            {
                check.Status = CheckStatus.Fail;
                check.Message = $"SAM status not in allowed list: {data.SAMStatus}";
            }

            return check;
        }

        private ComplianceCheck DebarmentCheck(ComplianceRegistryData data, bool registryFetchFailed)
        {
            var check = new ComplianceCheck { CheckType = CheckType.Debarment };

            if (data == null)
            {
                // If registry fetch failed, fail closed to prevent bypassing debarment verification
                if (registryFetchFailed)
                {
                    check.Status = CheckStatus.Fail;
                    check.Message = "CRITICAL: Registry lookup failed - debarment verification cannot be bypassed";
                    check.Details["registryFetchFailed"] = true;
                    return check;
                }
                check.Status = CheckStatus.Warning;
                check.Message = "No debarment data available";
                return check;
            }

            check.Details["debarred"] = data.Debarred;

            if (data.Debarred)
            {
                check.Status = CheckStatus.Fail;
                check.Message = "CRITICAL: Vendor is debarred";
            }
            else
            {
                check.Status = CheckStatus.Pass;
                check.Message = "Not debarred";
            }

            return check;
        }

        private ComplianceCheck OSHAViolationsCheck(ComplianceRegistryData data, bool registryFetchFailed)
        {
            var check = new ComplianceCheck { CheckType = CheckType.OSHAViolations };

            if (data == null)
            {
                // If registry fetch failed, fail closed to prevent bypassing OSHA verification
                if (registryFetchFailed)
                {
                    check.Status = CheckStatus.Fail;
                    check.Message = "CRITICAL: Registry lookup failed - OSHA verification cannot be bypassed";
                    check.Details["registryFetchFailed"] = true;
                    return check;
                }
                check.Status = CheckStatus.Warning;
                check.Message = "No OSHA data available";
                return check;
            }

            var violationCount = data.OSHAViolationCount ?? 0;
            check.Details["violationCount"] = violationCount;
            check.Details["lastInspection"] = data.OSHALastInspection?.ToString("yyyy-MM-dd") ?? "N/A";

            if (violationCount > _config.MaxOSHAViolations)
            {
                check.Status = CheckStatus.Fail;
                check.Message = $"{violationCount} OSHA violations (exceeds max of {_config.MaxOSHAViolations})";
            }
            else if (violationCount > _config.OSHAWarningThreshold)
            {
                check.Status = CheckStatus.Warning;
                check.Message = $"{violationCount} OSHA violations (above threshold of {_config.OSHAWarningThreshold})";
            }
            else if (violationCount == 0)
            {
                check.Status = CheckStatus.Pass;
                check.Message = "No OSHA violations";
            }
            else
            {
                check.Status = CheckStatus.Pass;
                check.Message = $"{violationCount} OSHA violation(s) within acceptable range";
            }

            return check;
        }

        private ComplianceCheck ERPStatusCheck(ERPVendorData data)
        {
            var check = new ComplianceCheck { CheckType = CheckType.ERPStatus };

            if (data == null)
            {
                check.Status = CheckStatus.Warning;
                check.Message = "No ERP data available";
                return check;
            }

            check.Details["erpStatus"] = data.ERPStatus ?? "Unknown";

            if (string.IsNullOrEmpty(data.ERPStatus))
            {
                check.Status = CheckStatus.Warning;
                check.Message = "ERP status not recorded";
                return check;
            }

            if (string.Equals(data.ERPStatus, "Hold", StringComparison.OrdinalIgnoreCase))
            {
                check.Status = CheckStatus.Fail;
                check.Message = "Vendor on hold in ERP";
            }
            else if (string.Equals(data.ERPStatus, "Inactive", StringComparison.OrdinalIgnoreCase))
            {
                check.Status = CheckStatus.Fail;
                check.Message = "Vendor inactive in ERP";
            }
            else if (string.Equals(data.ERPStatus, "Active", StringComparison.OrdinalIgnoreCase))
            {
                check.Status = CheckStatus.Pass;
                check.Message = "Active in ERP";
            }
            else
            {
                check.Status = CheckStatus.Warning;
                check.Message = $"Unknown ERP status: {data.ERPStatus}";
            }

            return check;
        }

        private string DetermineOverallStatus(List<ComplianceCheck> checks)
        {
            if (checks.Any(c => c.Status == CheckStatus.Fail))
                return CheckStatus.Fail;

            if (checks.Any(c => c.Status == CheckStatus.Warning))
                return CheckStatus.Warning;

            if (checks.All(c => c.Status == CheckStatus.Skipped))
                return CheckStatus.Warning;

            return CheckStatus.Pass;
        }

        #endregion
    }
}
