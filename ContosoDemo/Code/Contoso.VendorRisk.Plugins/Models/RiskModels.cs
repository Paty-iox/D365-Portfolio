using System;
using System.Collections.Generic;

namespace Contoso.VendorRisk.Plugins.Models
{
    public static class ConfigurationSource
    {
        public const string Database = "Database";
        public const string Defaults = "Defaults";
        public const string DefaultsDueToError = "Defaults (Error)";
        public const string DefaultsDueToMissing = "Defaults (Not Found)";
        public const string DefaultsDueToEmpty = "Defaults (Empty Config)";
    }

    public class RiskCalculationInput
    {
        public Guid VendorId { get; set; }
        public DateTime AssessmentDate { get; set; }
        public bool IncludeHistoricalTrend { get; set; }
    }

    public class RiskCalculationResult
    {
        // Core risk assessment results
        public decimal RiskScore { get; set; }
        public string RiskCategory { get; set; }
        public List<RiskFactor> RiskFactors { get; set; }
        public DateTime NextReviewDate { get; set; }

        // Component scores (0-100 each)
        public decimal ComplianceScore { get; set; }
        public decimal PaymentScore { get; set; }
        public decimal TenureScore { get; set; }
        public decimal DocumentationScore { get; set; }

        // Comparison with previous assessment
        public decimal? PreviousRiskScore { get; set; }
        public string PreviousRiskCategory { get; set; }
        public decimal? ScoreChange { get; set; }

        // Recommendations
        public List<Recommendation> Recommendations { get; set; }

        // Configuration observability
        public string ConfigSource { get; set; }
        public string ConfigWarning { get; set; }

        public RiskCalculationResult()
        {
            RiskFactors = new List<RiskFactor>();
            Recommendations = new List<Recommendation>();
        }
    }

    public class Recommendation
    {
        public string Priority { get; set; }
        public string Category { get; set; }
        public string Action { get; set; }
        public string Reason { get; set; }
    }

    public class PreviousAssessmentData
    {
        public Guid AssessmentId { get; set; }
        public decimal RiskScore { get; set; }
        public string RiskCategory { get; set; }
        public DateTime AssessmentDate { get; set; }
    }

    public class RiskFactor
    {
        public string Category { get; set; }
        public string Factor { get; set; }
        public string Impact { get; set; }
        public decimal ScoreImpact { get; set; }
        public string Details { get; set; }
    }

    public class VendorData
    {
        public Guid VendorId { get; set; }
        public string VendorNumber { get; set; }
        public string VendorName { get; set; }
        public int? YearsInBusiness { get; set; }
        public DateTime? OnboardingDate { get; set; }
        public int? RiskCategoryValue { get; set; }
    }

    public class ComplianceData
    {
        public int TotalDocuments { get; set; }
        public int CurrentDocuments { get; set; }
        public int ExpiredDocuments { get; set; }
        public int ExpiringSoonDocuments { get; set; }
        public int MissingRequiredDocuments { get; set; }
        /// <summary>
        /// Documents that are active but missing an expiration date - these represent unknown compliance status
        /// </summary>
        public int MissingExpirationDocuments { get; set; }
    }

    public class ERPVendorData
    {
        public string VendorNumber { get; set; }
        public string PaymentTerms { get; set; }
        public decimal? CreditLimit { get; set; }
        public decimal? YtdPurchases { get; set; }
        public int? OpenPOCount { get; set; }
        public decimal? OpenPOValue { get; set; }
        public int? AvgDaysToPay { get; set; }
        public DateTime? LastPaymentDate { get; set; }
        public string ERPStatus { get; set; }
    }

    public class ComplianceRegistryData
    {
        public string VendorNumber { get; set; }
        public string SAMStatus { get; set; }
        public DateTime? SAMExpiry { get; set; }
        public int? OSHAViolationCount { get; set; }
        public DateTime? OSHALastInspection { get; set; }
        public bool Debarred { get; set; }
    }

    // TODO: Future enhancements for config robustness:
    // 1. Add Validate() method to RiskScoringConfig that returns list of validation errors
    // 2. Add contoso_isdraft field to table for draft/active config pattern
    // 3. Add contoso_version field for config versioning and rollback support
    // 4. Create separate validation plugin on contoso_apiconfig (PreCreate/PreUpdate)
    // 5. Add contoso_validationerrors field to store validation results on the record

    public class RiskScoringConfig
    {
        public WeightsConfig Weights { get; set; }
        public ThresholdsConfig Thresholds { get; set; }
        public DeductionsConfig Deductions { get; set; }
        public ReviewDaysConfig ReviewDays { get; set; }
        public TenureScoresConfig TenureScores { get; set; }
        public int? DocumentExpiryWarningDays { get; set; }

        public RiskScoringConfig()
        {
            Weights = new WeightsConfig();
            Thresholds = new ThresholdsConfig();
            Deductions = new DeductionsConfig();
            ReviewDays = new ReviewDaysConfig();
            TenureScores = new TenureScoresConfig();
        }

        public static RiskScoringConfig GetDefaults()
        {
            return new RiskScoringConfig
            {
                Weights = new WeightsConfig { Compliance = 0.35m, Payment = 0.25m, Tenure = 0.20m, Documentation = 0.20m },
                Thresholds = new ThresholdsConfig { Low = 80m, Medium = 60m, High = 40m },
                Deductions = new DeductionsConfig
                {
                    ExpiredDocumentPer = 15, ExpiredDocumentMax = 45,
                    ExpiringSoonPer = 5, ExpiringSoonMax = 25,
                    Debarred = 100, SamExpired = 30, SamInactive = 20,
                    OshaViolationPer = 5, OshaViolationMax = 25,
                    ErpOnHold = 30, PaymentSlow45 = 10, PaymentSlow60 = 25, PaymentSlow60Plus = 40,
                    NoDocuments = 50, MissingVendorNumber = 15
                },
                ReviewDays = new ReviewDaysConfig { Critical = 7, High = 30, Medium = 90, Low = 180 },
                TenureScores = new TenureScoresConfig
                {
                    Established = new TenureTier { Years = 10, Score = 100m },
                    Mature = new TenureTier { Years = 5, Score = 85m },
                    Growing = new TenureTier { Years = 2, Score = 65m },
                    New = new TenureTier { Years = 0, Score = 40m }
                },
                DocumentExpiryWarningDays = 30
            };
        }

        public void MergeWithDefaults()
        {
            var defaults = GetDefaults();

            // Use null-coalescing to allow explicit zero values in config
            if (Weights == null) Weights = defaults.Weights;
            else
            {
                Weights.Compliance = Weights.Compliance ?? defaults.Weights.Compliance;
                Weights.Payment = Weights.Payment ?? defaults.Weights.Payment;
                Weights.Tenure = Weights.Tenure ?? defaults.Weights.Tenure;
                Weights.Documentation = Weights.Documentation ?? defaults.Weights.Documentation;
            }

            if (Thresholds == null) Thresholds = defaults.Thresholds;
            else
            {
                Thresholds.Low = Thresholds.Low ?? defaults.Thresholds.Low;
                Thresholds.Medium = Thresholds.Medium ?? defaults.Thresholds.Medium;
                Thresholds.High = Thresholds.High ?? defaults.Thresholds.High;
            }

            if (Deductions == null) Deductions = defaults.Deductions;
            else
            {
                Deductions.ExpiredDocumentPer = Deductions.ExpiredDocumentPer ?? defaults.Deductions.ExpiredDocumentPer;
                Deductions.ExpiredDocumentMax = Deductions.ExpiredDocumentMax ?? defaults.Deductions.ExpiredDocumentMax;
                Deductions.ExpiringSoonPer = Deductions.ExpiringSoonPer ?? defaults.Deductions.ExpiringSoonPer;
                Deductions.ExpiringSoonMax = Deductions.ExpiringSoonMax ?? defaults.Deductions.ExpiringSoonMax;
                Deductions.Debarred = Deductions.Debarred ?? defaults.Deductions.Debarred;
                Deductions.SamExpired = Deductions.SamExpired ?? defaults.Deductions.SamExpired;
                Deductions.SamInactive = Deductions.SamInactive ?? defaults.Deductions.SamInactive;
                Deductions.OshaViolationPer = Deductions.OshaViolationPer ?? defaults.Deductions.OshaViolationPer;
                Deductions.OshaViolationMax = Deductions.OshaViolationMax ?? defaults.Deductions.OshaViolationMax;
                Deductions.ErpOnHold = Deductions.ErpOnHold ?? defaults.Deductions.ErpOnHold;
                Deductions.PaymentSlow45 = Deductions.PaymentSlow45 ?? defaults.Deductions.PaymentSlow45;
                Deductions.PaymentSlow60 = Deductions.PaymentSlow60 ?? defaults.Deductions.PaymentSlow60;
                Deductions.PaymentSlow60Plus = Deductions.PaymentSlow60Plus ?? defaults.Deductions.PaymentSlow60Plus;
                Deductions.NoDocuments = Deductions.NoDocuments ?? defaults.Deductions.NoDocuments;
                Deductions.MissingVendorNumber = Deductions.MissingVendorNumber ?? defaults.Deductions.MissingVendorNumber;
            }

            if (ReviewDays == null) ReviewDays = defaults.ReviewDays;
            else
            {
                ReviewDays.Critical = ReviewDays.Critical ?? defaults.ReviewDays.Critical;
                ReviewDays.High = ReviewDays.High ?? defaults.ReviewDays.High;
                ReviewDays.Medium = ReviewDays.Medium ?? defaults.ReviewDays.Medium;
                ReviewDays.Low = ReviewDays.Low ?? defaults.ReviewDays.Low;
            }

            if (TenureScores == null) TenureScores = defaults.TenureScores;
            else
            {
                if (TenureScores.Established == null) TenureScores.Established = defaults.TenureScores.Established;
                else
                {
                    TenureScores.Established.Years = TenureScores.Established.Years ?? defaults.TenureScores.Established.Years;
                    TenureScores.Established.Score = TenureScores.Established.Score ?? defaults.TenureScores.Established.Score;
                }

                if (TenureScores.Mature == null) TenureScores.Mature = defaults.TenureScores.Mature;
                else
                {
                    TenureScores.Mature.Years = TenureScores.Mature.Years ?? defaults.TenureScores.Mature.Years;
                    TenureScores.Mature.Score = TenureScores.Mature.Score ?? defaults.TenureScores.Mature.Score;
                }

                if (TenureScores.Growing == null) TenureScores.Growing = defaults.TenureScores.Growing;
                else
                {
                    TenureScores.Growing.Years = TenureScores.Growing.Years ?? defaults.TenureScores.Growing.Years;
                    TenureScores.Growing.Score = TenureScores.Growing.Score ?? defaults.TenureScores.Growing.Score;
                }

                if (TenureScores.New == null) TenureScores.New = defaults.TenureScores.New;
                else
                {
                    TenureScores.New.Years = TenureScores.New.Years ?? defaults.TenureScores.New.Years;
                    TenureScores.New.Score = TenureScores.New.Score ?? defaults.TenureScores.New.Score;
                }
            }

            // Default DocumentExpiryWarningDays
            DocumentExpiryWarningDays = DocumentExpiryWarningDays ?? defaults.DocumentExpiryWarningDays;
        }

        public decimal GetWeightSum()
        {
            if (Weights == null) return 0;
            return (Weights.Compliance ?? 0) + (Weights.Payment ?? 0) + (Weights.Tenure ?? 0) + (Weights.Documentation ?? 0);
        }
    }

    public class WeightsConfig
    {
        public decimal? Compliance { get; set; }
        public decimal? Payment { get; set; }
        public decimal? Tenure { get; set; }
        public decimal? Documentation { get; set; }
    }

    public class ThresholdsConfig
    {
        public decimal? Low { get; set; }
        public decimal? Medium { get; set; }
        public decimal? High { get; set; }
    }

    public class DeductionsConfig
    {
        public decimal? ExpiredDocumentPer { get; set; }
        public decimal? ExpiredDocumentMax { get; set; }
        public decimal? ExpiringSoonPer { get; set; }
        public decimal? ExpiringSoonMax { get; set; }
        public decimal? Debarred { get; set; }
        public decimal? SamExpired { get; set; }
        public decimal? SamInactive { get; set; }
        public decimal? OshaViolationPer { get; set; }
        public decimal? OshaViolationMax { get; set; }
        public decimal? ErpOnHold { get; set; }
        public decimal? PaymentSlow45 { get; set; }
        public decimal? PaymentSlow60 { get; set; }
        public decimal? PaymentSlow60Plus { get; set; }
        public decimal? NoDocuments { get; set; }
        public decimal? MissingVendorNumber { get; set; }
    }

    public class ReviewDaysConfig
    {
        public int? Critical { get; set; }
        public int? High { get; set; }
        public int? Medium { get; set; }
        public int? Low { get; set; }
    }

    public class TenureScoresConfig
    {
        public TenureTier Established { get; set; }
        public TenureTier Mature { get; set; }
        public TenureTier Growing { get; set; }
        public TenureTier New { get; set; }
    }

    public class TenureTier
    {
        public int? Years { get; set; }
        public decimal? Score { get; set; }
    }
}
