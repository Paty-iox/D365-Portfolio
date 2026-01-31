using System;
using System.Collections.Generic;

namespace Contoso.VendorRisk.Plugins.Models
{
    public class BulkComplianceInput
    {
        public List<Guid> VendorIds { get; set; }
        public List<string> CheckTypes { get; set; }
        public bool FailFast { get; set; }

        public BulkComplianceInput()
        {
            VendorIds = new List<Guid>();
            CheckTypes = new List<string>();
            FailFast = false;
        }
    }

    public class BulkComplianceResult
    {
        public int TotalVendors { get; set; }
        public int RequestedCount { get; set; }
        public int ProcessedCount { get; set; }
        public int PassedCount { get; set; }
        public int FailedCount { get; set; }
        public bool FailFastTriggered { get; set; }
        public List<Guid> SkippedVendorIds { get; set; }
        public List<VendorComplianceResult> Results { get; set; }
        public long ExecutionTimeMs { get; set; }

        // Configuration observability
        public string ConfigSource { get; set; }
        public string ConfigWarning { get; set; }

        public BulkComplianceResult()
        {
            Results = new List<VendorComplianceResult>();
            SkippedVendorIds = new List<Guid>();
        }
    }

    public class VendorComplianceResult
    {
        public Guid VendorId { get; set; }
        public string VendorNumber { get; set; }
        public string VendorName { get; set; }
        public string OverallStatus { get; set; }
        public List<ComplianceCheck> Checks { get; set; }
        public int FailedChecks { get; set; }
        public int WarningChecks { get; set; }
        public string ErrorMessage { get; set; }

        public VendorComplianceResult()
        {
            Checks = new List<ComplianceCheck>();
        }
    }

    public class ComplianceCheck
    {
        public string CheckType { get; set; }
        public string Status { get; set; }
        public string Message { get; set; }
        public Dictionary<string, object> Details { get; set; }

        public ComplianceCheck()
        {
            Details = new Dictionary<string, object>();
        }
    }

    public static class CheckStatus
    {
        public const string Pass = "Pass";
        public const string Fail = "Fail";
        public const string Warning = "Warning";
        public const string Skipped = "Skipped";
    }

    public static class CheckType
    {
        public const string DocumentExpiry = "DocumentExpiry";
        public const string SAMStatus = "SAMStatus";
        public const string Debarment = "Debarment";
        public const string OSHAViolations = "OSHAViolations";
        public const string ERPStatus = "ERPStatus";

        public static readonly List<string> All = new List<string>
        {
            DocumentExpiry,
            SAMStatus,
            Debarment,
            OSHAViolations,
            ERPStatus
        };
    }

    public class BulkComplianceConfig
    {
        public int MaxVendorsPerCall { get; set; }
        public int DocumentExpiryWarningDays { get; set; }
        public int MaxOSHAViolations { get; set; }
        public int OSHAWarningThreshold { get; set; }
        public List<string> AllowedSAMStatuses { get; set; }
        public List<string> EnabledCheckTypes { get; set; }

        public BulkComplianceConfig()
        {
            AllowedSAMStatuses = new List<string>();
            EnabledCheckTypes = new List<string>();
        }

        public static BulkComplianceConfig GetDefaults()
        {
            return new BulkComplianceConfig
            {
                MaxVendorsPerCall = 100,
                DocumentExpiryWarningDays = 30,
                MaxOSHAViolations = 3,
                OSHAWarningThreshold = 1,
                AllowedSAMStatuses = new List<string> { "Active" },
                EnabledCheckTypes = new List<string>(CheckType.All)
            };
        }

        public void MergeWithDefaults()
        {
            var defaults = GetDefaults();

            if (MaxVendorsPerCall <= 0) MaxVendorsPerCall = defaults.MaxVendorsPerCall;
            if (DocumentExpiryWarningDays <= 0) DocumentExpiryWarningDays = defaults.DocumentExpiryWarningDays;
            if (MaxOSHAViolations <= 0) MaxOSHAViolations = defaults.MaxOSHAViolations;
            if (OSHAWarningThreshold < 0) OSHAWarningThreshold = defaults.OSHAWarningThreshold;
            if (AllowedSAMStatuses == null || AllowedSAMStatuses.Count == 0)
                AllowedSAMStatuses = defaults.AllowedSAMStatuses;
            if (EnabledCheckTypes == null || EnabledCheckTypes.Count == 0)
                EnabledCheckTypes = defaults.EnabledCheckTypes;
        }
    }
}
