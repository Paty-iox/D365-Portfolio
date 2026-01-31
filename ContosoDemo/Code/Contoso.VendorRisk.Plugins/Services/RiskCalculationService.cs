using System;
using System.Collections.Generic;
using System.ServiceModel;
using Contoso.VendorRisk.Plugins.Models;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Newtonsoft.Json;

namespace Contoso.VendorRisk.Plugins.Services
{
    public class RiskCalculationService
    {
        private readonly IOrganizationService _service;
        private readonly ITracingService _tracingService;
        private readonly RiskScoringConfig _config;
        private readonly string _configSource;
        private readonly string _configWarning;

        private const int RISK_SCORING_CONFIG_TYPE = 100000000;

        public RiskCalculationService(IOrganizationService service, ITracingService tracingService)
        {
            _service = service;
            _tracingService = tracingService;
            _config = LoadConfig(out _configSource, out _configWarning);
        }

        public string ConfigSource => _configSource;
        public string ConfigWarning => _configWarning;

        private RiskScoringConfig LoadConfig(out string configSource, out string configWarning)
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
                            new ConditionExpression("contoso_configtype", ConditionOperator.Equal, RISK_SCORING_CONFIG_TYPE),
                            new ConditionExpression("contoso_isactive", ConditionOperator.Equal, true),
                            new ConditionExpression("statecode", ConditionOperator.Equal, 0)
                        }
                    },
                    TopCount = 1
                };

                var results = _service.RetrieveMultiple(query);
                if (results.Entities.Count == 0)
                {
                    _tracingService.Trace("WARNING: No active Risk Scoring config found - using defaults. Scoring behavior may differ from expected.");
                    configSource = ConfigurationSource.DefaultsDueToMissing;
                    configWarning = "No active Risk Scoring configuration found in contoso_apiconfig. Using default values.";
                    return RiskScoringConfig.GetDefaults();
                }

                var configJson = results.Entities[0].GetAttributeValue<string>("contoso_configjson");
                if (string.IsNullOrEmpty(configJson))
                {
                    _tracingService.Trace("WARNING: Config JSON is empty - using defaults. Scoring behavior may differ from expected.");
                    configSource = ConfigurationSource.DefaultsDueToEmpty;
                    configWarning = "Risk Scoring configuration record exists but JSON is empty. Using default values.";
                    return RiskScoringConfig.GetDefaults();
                }

                var config = JsonConvert.DeserializeObject<RiskScoringConfig>(configJson);

                // Fill in any missing values from defaults (handles partial JSON and null nested objects)
                config.MergeWithDefaults();

                // Validate weights sum to ~1.0
                var weightSum = config.GetWeightSum();
                if (Math.Abs(weightSum - 1.0m) > 0.01m)
                {
                    _tracingService.Trace("WARNING: Config weights sum to {0}, expected 1.0. Scores may be incorrect.", weightSum);
                    configWarning = $"Configuration weights sum to {weightSum:F2} instead of 1.0. Scores may be skewed.";
                }

                // TODO: Add additional config validation:
                // - Ensure all deduction values are non-negative
                // - Verify thresholds are in descending order (Low > Medium > High > 0)
                // - Check tenure years are in descending order (Established > Mature > Growing > New)
                // - Validate review days are positive integers
                // - Consider implementing a separate validation plugin on contoso_apiconfig PreCreate/PreUpdate

                _tracingService.Trace("Loaded Risk Scoring config from Dataverse");
                configSource = ConfigurationSource.Database;
                return config;
            }
            catch (Exception ex)
            {
                _tracingService.Trace("ERROR loading config: {0} - using defaults. Scoring behavior may differ from expected.", ex.Message);
                configSource = ConfigurationSource.DefaultsDueToError;
                configWarning = $"Error loading Risk Scoring configuration: {ex.Message}. Using default values.";
                return RiskScoringConfig.GetDefaults();
            }
        }

        public RiskCalculationResult CalculateRisk(RiskCalculationInput input)
        {
            _tracingService.Trace("Starting risk calculation for Vendor: {0}", input.VendorId);
            _tracingService.Trace("Using weights - Compliance: {0}, Payment: {1}, Tenure: {2}, Doc: {3}",
                _config.Weights.Compliance, _config.Weights.Payment, _config.Weights.Tenure, _config.Weights.Documentation);

            var result = new RiskCalculationResult();

            var vendorData = GetVendorData(input.VendorId);
            if (vendorData == null)
            {
                throw new InvalidPluginExecutionException($"Vendor with ID {input.VendorId} not found.");
            }

            _tracingService.Trace("Vendor found: {0} ({1})", vendorData.VendorName, vendorData.VendorNumber ?? "NO VENDOR NUMBER");

            var complianceData = GetComplianceData(input.VendorId, input.AssessmentDate);
            _tracingService.Trace("Compliance: {0} total, {1} current, {2} expired, {3} missing expiry date",
                complianceData.TotalDocuments, complianceData.CurrentDocuments, complianceData.ExpiredDocuments, complianceData.MissingExpirationDocuments);

            ERPVendorData erpData = null;
            ComplianceRegistryData registryData = null;
            decimal missingVendorNumberPenalty = 0m;

            if (string.IsNullOrEmpty(vendorData.VendorNumber))
            {
                _tracingService.Trace("Vendor has no VendorNumber - skipping ERP and Registry lookups");
                missingVendorNumberPenalty = _config.Deductions.MissingVendorNumber ?? 0;
                result.RiskFactors.Add(new RiskFactor
                {
                    Category = "Data Quality",
                    Factor = "Missing Vendor Number",
                    Impact = "Negative",
                    ScoreImpact = -missingVendorNumberPenalty,
                    Details = "Vendor record is missing VendorNumber - unable to retrieve ERP and compliance registry data"
                });
            }
            else
            {
                erpData = GetERPVendorData(vendorData.VendorNumber);
                registryData = GetComplianceRegistryData(vendorData.VendorNumber);
            }

            _tracingService.Trace("ERP Data: Avg Days to Pay = {0}", erpData?.AvgDaysToPay ?? 0);
            _tracingService.Trace("Registry: SAM Status = {0}, Debarred = {1}",
                registryData?.SAMStatus ?? "N/A", registryData?.Debarred ?? false);

            var complianceScore = CalculateComplianceScore(complianceData, registryData, result.RiskFactors);
            var paymentScore = CalculatePaymentScore(erpData, result.RiskFactors);
            var tenureScore = CalculateTenureScore(vendorData, result.RiskFactors, input.AssessmentDate);
            var documentationScore = CalculateDocumentationScore(complianceData, result.RiskFactors);

            // Store component scores in result
            result.ComplianceScore = Math.Round(complianceScore, 2);
            result.PaymentScore = Math.Round(paymentScore, 2);
            result.TenureScore = Math.Round(tenureScore, 2);
            result.DocumentationScore = Math.Round(documentationScore, 2);

            _tracingService.Trace("Scores - Compliance: {0}, Payment: {1}, Tenure: {2}, Documentation: {3}",
                complianceScore, paymentScore, tenureScore, documentationScore);

            var weightedScore =
                (complianceScore * (_config.Weights.Compliance ?? 0)) +
                (paymentScore * (_config.Weights.Payment ?? 0)) +
                (tenureScore * (_config.Weights.Tenure ?? 0)) +
                (documentationScore * (_config.Weights.Documentation ?? 0));

            // Apply missing vendor number penalty to final score
            var finalScore = Math.Max(Math.Round(weightedScore - missingVendorNumberPenalty, 2), 0);

            // CRITICAL: Debarred vendors must have RiskScore forced to 0
            // This ensures downstream systems using numeric scores don't treat debarred vendors as acceptable
            if (registryData?.Debarred == true)
            {
                _tracingService.Trace("Vendor is DEBARRED - forcing RiskScore to 0 (was {0})", finalScore);
                result.RiskScore = 0;
            }
            else
            {
                result.RiskScore = finalScore;
            }

            result.RiskCategory = DetermineRiskCategory(result.RiskScore, registryData);
            result.NextReviewDate = CalculateNextReviewDate(result.RiskCategory, input.AssessmentDate);

            // Get previous assessment for comparison
            var previousAssessment = GetPreviousAssessment(input.VendorId, input.AssessmentDate);
            if (previousAssessment != null)
            {
                result.PreviousRiskScore = previousAssessment.RiskScore;
                result.PreviousRiskCategory = previousAssessment.RiskCategory;
                result.ScoreChange = result.RiskScore - previousAssessment.RiskScore;
                _tracingService.Trace("Previous assessment found - Score: {0}, Category: {1}, Change: {2}",
                    previousAssessment.RiskScore, previousAssessment.RiskCategory, result.ScoreChange);
            }
            else
            {
                _tracingService.Trace("No previous assessment found for vendor");
            }

            // Generate recommendations based on risk factors
            result.Recommendations = GenerateRecommendations(result.RiskFactors, complianceData, registryData, erpData);
            _tracingService.Trace("Generated {0} recommendations", result.Recommendations.Count);

            // Include configuration observability info
            result.ConfigSource = _configSource;
            result.ConfigWarning = _configWarning;

            _tracingService.Trace("Final Score: {0}, Category: {1}", result.RiskScore, result.RiskCategory);

            return result;
        }

        private VendorData GetVendorData(Guid vendorId)
        {
            try
            {
                var entity = _service.Retrieve("contoso_vendor", vendorId, new ColumnSet(
                    "contoso_vendornumber",
                    "contoso_vendorname",
                    "contoso_yearsinbusiness",
                    "contoso_onboardingdate",
                    "contoso_riskcategory"
                ));

                return new VendorData
                {
                    VendorId = vendorId,
                    VendorNumber = entity.GetAttributeValue<string>("contoso_vendornumber"),
                    VendorName = entity.GetAttributeValue<string>("contoso_vendorname"),
                    YearsInBusiness = entity.GetAttributeValue<int?>("contoso_yearsinbusiness"),
                    OnboardingDate = entity.GetAttributeValue<DateTime?>("contoso_onboardingdate"),
                    RiskCategoryValue = entity.GetAttributeValue<OptionSetValue>("contoso_riskcategory")?.Value
                };
            }
            catch (FaultException<OrganizationServiceFault> ex) when (ex.Detail?.ErrorCode == -2147220969)
            {
                _tracingService.Trace("Vendor not found: {0}", vendorId);
                return null;
            }
            catch (Exception ex)
            {
                _tracingService.Trace("Error retrieving vendor: {0}\n{1}", ex.Message, ex.StackTrace);
                throw new InvalidPluginExecutionException($"Failed to retrieve vendor data: {ex.Message}", ex);
            }
        }

        private ComplianceData GetComplianceData(Guid vendorId, DateTime assessmentDate)
        {
            var result = new ComplianceData();
            var today = assessmentDate.Date;
            var warningDays = _config.DocumentExpiryWarningDays ?? 30;
            var warningDate = today.AddDays(warningDays);

            var query = new QueryExpression("contoso_compliancerecord")
            {
                ColumnSet = new ColumnSet("contoso_expirationdate", "statecode"),
                Criteria = new FilterExpression
                {
                    Conditions =
                    {
                        new ConditionExpression("contoso_vendor", ConditionOperator.Equal, vendorId)
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

            do
            {
                records = _service.RetrieveMultiple(query);

                foreach (var record in records.Entities)
                {
                    result.TotalDocuments++;
                    var expirationDate = record.GetAttributeValue<DateTime?>("contoso_expirationdate");
                    var stateCode = record.GetAttributeValue<OptionSetValue>("statecode")?.Value ?? 0;

                    if (stateCode == 0) // Active document
                    {
                        if (expirationDate.HasValue)
                        {
                            if (expirationDate.Value >= today)
                            {
                                result.CurrentDocuments++;
                                if (expirationDate.Value <= warningDate)
                                {
                                    result.ExpiringSoonDocuments++;
                                }
                            }
                            else
                            {
                                result.ExpiredDocuments++;
                            }
                        }
                        else
                        {
                            // Active document without expiration date - unknown compliance status
                            result.MissingExpirationDocuments++;
                        }
                    }
                    else if (stateCode == 1) // Inactive document
                    {
                        result.ExpiredDocuments++;
                    }
                }

                // Set up next page
                if (records.MoreRecords)
                {
                    query.PageInfo.PageNumber++;
                    query.PageInfo.PagingCookie = records.PagingCookie;
                }

            } while (records.MoreRecords);

            if (result.TotalDocuments > 5000)
            {
                _tracingService.Trace("Paged through {0} compliance records for vendor {1}", result.TotalDocuments, vendorId);
            }

            return result;
        }

        private ERPVendorData GetERPVendorData(string vendorNumber)
        {
            if (string.IsNullOrEmpty(vendorNumber)) return null;

            try
            {
                var query = new QueryExpression("contoso_erpvendormaster")
                {
                    ColumnSet = new ColumnSet(
                        "contoso_name",
                        "contoso_paymentterms",
                        "contoso_creditlimit",
                        "contoso_ytdpurchases",
                        "contoso_openpocount",
                        "contoso_openpovalue",
                        "contoso_avgdaystopay",
                        "contoso_lastpaymentdate",
                        "contoso_erpstatus"
                    ),
                    Criteria = new FilterExpression
                    {
                        Conditions =
                        {
                            new ConditionExpression("contoso_name", ConditionOperator.Equal, vendorNumber)
                        }
                    },
                    TopCount = 1
                };

                var results = _service.RetrieveMultiple(query);
                if (results.Entities.Count == 0)
                {
                    _tracingService.Trace("No ERP data found for vendor: {0}", vendorNumber);
                    return null;
                }

                var entity = results.Entities[0];
                return new ERPVendorData
                {
                    VendorNumber = entity.GetAttributeValue<string>("contoso_name"),
                    PaymentTerms = entity.GetAttributeValue<string>("contoso_paymentterms"),
                    CreditLimit = entity.GetAttributeValue<decimal?>("contoso_creditlimit"),
                    YtdPurchases = entity.GetAttributeValue<decimal?>("contoso_ytdpurchases"),
                    OpenPOCount = entity.GetAttributeValue<int?>("contoso_openpocount"),
                    OpenPOValue = entity.GetAttributeValue<decimal?>("contoso_openpovalue"),
                    AvgDaysToPay = entity.GetAttributeValue<int?>("contoso_avgdaystopay"),
                    LastPaymentDate = entity.GetAttributeValue<DateTime?>("contoso_lastpaymentdate"),
                    ERPStatus = entity.GetAttributeValue<string>("contoso_erpstatus")
                };
            }
            catch (FaultException<OrganizationServiceFault> ex)
            {
                _tracingService.Trace("ERP Virtual Table error for {0}: {1}\n{2}", vendorNumber, ex.Message, ex.StackTrace);
                return null;
            }
            catch (Exception ex)
            {
                _tracingService.Trace("Unexpected error retrieving ERP data for {0}: {1}\n{2}", vendorNumber, ex.Message, ex.StackTrace);
                return null;
            }
        }

        private ComplianceRegistryData GetComplianceRegistryData(string vendorNumber)
        {
            if (string.IsNullOrEmpty(vendorNumber)) return null;

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
                            new ConditionExpression("contoso_name", ConditionOperator.Equal, vendorNumber)
                        }
                    },
                    TopCount = 1
                };

                var results = _service.RetrieveMultiple(query);
                if (results.Entities.Count == 0)
                {
                    _tracingService.Trace("No compliance registry data found for vendor: {0}", vendorNumber);
                    return null;
                }

                var entity = results.Entities[0];
                return new ComplianceRegistryData
                {
                    VendorNumber = entity.GetAttributeValue<string>("contoso_name"),
                    SAMStatus = entity.GetAttributeValue<string>("contoso_samstatus"),
                    SAMExpiry = entity.GetAttributeValue<DateTime?>("contoso_samexpiry"),
                    OSHAViolationCount = entity.GetAttributeValue<int?>("contoso_oshaviolationcount"),
                    OSHALastInspection = entity.GetAttributeValue<DateTime?>("contoso_oshalastinspection"),
                    Debarred = entity.GetAttributeValue<bool?>("contoso_debarred") ?? false
                };
            }
            catch (FaultException<OrganizationServiceFault> ex)
            {
                _tracingService.Trace("Compliance Registry Virtual Table error for {0}: {1}\n{2}", vendorNumber, ex.Message, ex.StackTrace);
                throw new InvalidPluginExecutionException(
                    $"Failed to retrieve compliance registry data for vendor {vendorNumber}. " +
                    "Debarment/SAM verification cannot be bypassed. Error: " + ex.Message, ex);
            }
            catch (Exception ex)
            {
                _tracingService.Trace("Unexpected error retrieving compliance registry for {0}: {1}\n{2}", vendorNumber, ex.Message, ex.StackTrace);
                throw new InvalidPluginExecutionException(
                    $"Failed to retrieve compliance registry data for vendor {vendorNumber}. " +
                    "Debarment/SAM verification cannot be bypassed. Error: " + ex.Message, ex);
            }
        }

        private decimal CalculateComplianceScore(ComplianceData compliance, ComplianceRegistryData registry, List<RiskFactor> factors)
        {
            decimal score = 100m;
            var warningDays = _config.DocumentExpiryWarningDays ?? 30;

            if (compliance.ExpiredDocuments > 0)
            {
                var deduction = Math.Min(compliance.ExpiredDocuments * (_config.Deductions.ExpiredDocumentPer ?? 0), _config.Deductions.ExpiredDocumentMax ?? 0);
                score -= deduction;
                factors.Add(new RiskFactor
                {
                    Category = "Compliance",
                    Factor = "Expired Documents",
                    Impact = "Negative",
                    ScoreImpact = -deduction,
                    Details = $"{compliance.ExpiredDocuments} expired document(s)"
                });
            }

            // Documents with missing expiration dates represent unknown compliance status - treat as warning
            if (compliance.MissingExpirationDocuments > 0)
            {
                // Apply half the per-document expired penalty for missing expiration dates
                var perDocPenalty = (_config.Deductions.ExpiredDocumentPer ?? 0) / 2;
                var deduction = Math.Min(compliance.MissingExpirationDocuments * perDocPenalty, (_config.Deductions.ExpiredDocumentMax ?? 0) / 2);
                score -= deduction;
                factors.Add(new RiskFactor
                {
                    Category = "Compliance",
                    Factor = "Missing Expiration Dates",
                    Impact = "Negative",
                    ScoreImpact = -deduction,
                    Details = $"{compliance.MissingExpirationDocuments} document(s) missing expiration date - compliance status unknown"
                });
            }

            if (compliance.ExpiringSoonDocuments > 0)
            {
                var deduction = Math.Min(compliance.ExpiringSoonDocuments * (_config.Deductions.ExpiringSoonPer ?? 0), _config.Deductions.ExpiringSoonMax ?? 0);
                score -= deduction;
                factors.Add(new RiskFactor
                {
                    Category = "Compliance",
                    Factor = "Documents Expiring Soon",
                    Impact = "Negative",
                    ScoreImpact = -deduction,
                    Details = $"{compliance.ExpiringSoonDocuments} document(s) expiring within {warningDays} days"
                });
            }

            if (registry != null)
            {
                if (registry.Debarred)
                {
                    score = 0;
                    factors.Add(new RiskFactor
                    {
                        Category = "Compliance",
                        Factor = "Debarred",
                        Impact = "Negative",
                        ScoreImpact = -(_config.Deductions.Debarred ?? 0),
                        Details = "Vendor is debarred - critical risk"
                    });
                }
                else if (!string.Equals(registry.SAMStatus, "Active", StringComparison.OrdinalIgnoreCase))
                {
                    var deduction = string.Equals(registry.SAMStatus, "Expired", StringComparison.OrdinalIgnoreCase)
                        ? (_config.Deductions.SamExpired ?? 0)
                        : (_config.Deductions.SamInactive ?? 0);
                    score -= deduction;
                    factors.Add(new RiskFactor
                    {
                        Category = "Compliance",
                        Factor = "SAM Status",
                        Impact = "Negative",
                        ScoreImpact = -deduction,
                        Details = $"SAM.gov status is {registry.SAMStatus}"
                    });
                }

                if (registry.OSHAViolationCount.HasValue && registry.OSHAViolationCount > 0)
                {
                    var deduction = Math.Min(registry.OSHAViolationCount.Value * (_config.Deductions.OshaViolationPer ?? 0), _config.Deductions.OshaViolationMax ?? 0);
                    score -= deduction;
                    factors.Add(new RiskFactor
                    {
                        Category = "Compliance",
                        Factor = "OSHA Violations",
                        Impact = "Negative",
                        ScoreImpact = -deduction,
                        Details = $"{registry.OSHAViolationCount} OSHA violation(s) on record"
                    });
                }
            }

            return Math.Max(score, 0);
        }

        private decimal CalculatePaymentScore(ERPVendorData erp, List<RiskFactor> factors)
        {
            decimal score = 100m;

            if (erp == null)
            {
                // No ERP data reduces score from 100 to 70 (30-point penalty)
                var penalty = 30m;
                factors.Add(new RiskFactor
                {
                    Category = "Payment",
                    Factor = "No ERP Data",
                    Impact = "Negative",
                    ScoreImpact = -penalty,
                    Details = "No ERP payment history available - cannot verify payment behavior"
                });
                return 100m - penalty;
            }

            if (erp.AvgDaysToPay.HasValue)
            {
                var avgDays = erp.AvgDaysToPay.Value;
                if (avgDays <= 30)
                {
                    factors.Add(new RiskFactor
                    {
                        Category = "Payment",
                        Factor = "Payment Timeliness",
                        Impact = "Positive",
                        ScoreImpact = 0,
                        Details = $"Excellent - Average {avgDays} days to pay"
                    });
                }
                else if (avgDays <= 45)
                {
                    var deduction = _config.Deductions.PaymentSlow45 ?? 0;
                    score -= deduction;
                    factors.Add(new RiskFactor
                    {
                        Category = "Payment",
                        Factor = "Payment Timeliness",
                        Impact = "Negative",
                        ScoreImpact = -deduction,
                        Details = $"Slightly slow - Average {avgDays} days to pay"
                    });
                }
                else if (avgDays <= 60)
                {
                    var deduction = _config.Deductions.PaymentSlow60 ?? 0;
                    score -= deduction;
                    factors.Add(new RiskFactor
                    {
                        Category = "Payment",
                        Factor = "Payment Timeliness",
                        Impact = "Negative",
                        ScoreImpact = -deduction,
                        Details = $"Slow payer - Average {avgDays} days to pay"
                    });
                }
                else
                {
                    var deduction = _config.Deductions.PaymentSlow60Plus ?? 0;
                    score -= deduction;
                    factors.Add(new RiskFactor
                    {
                        Category = "Payment",
                        Factor = "Payment Timeliness",
                        Impact = "Negative",
                        ScoreImpact = -deduction,
                        Details = $"Very slow payer - Average {avgDays} days to pay"
                    });
                }
            }
            else
            {
                // ERP record exists but no payment history data - cannot verify payment behavior
                // Apply a moderate penalty (15 points) since we can't confirm good payment behavior
                var penalty = 15m;
                score -= penalty;
                factors.Add(new RiskFactor
                {
                    Category = "Payment",
                    Factor = "Missing Payment History",
                    Impact = "Negative",
                    ScoreImpact = -penalty,
                    Details = "ERP record exists but payment history (AvgDaysToPay) is missing - cannot verify payment behavior"
                });
            }

            if (string.Equals(erp.ERPStatus, "Hold", StringComparison.OrdinalIgnoreCase))
            {
                var deduction = _config.Deductions.ErpOnHold ?? 0;
                score -= deduction;
                factors.Add(new RiskFactor
                {
                    Category = "Payment",
                    Factor = "ERP Status",
                    Impact = "Negative",
                    ScoreImpact = -deduction,
                    Details = "Vendor is on hold in ERP system"
                });
            }

            return Math.Max(score, 0);
        }

        private decimal CalculateTenureScore(VendorData vendor, List<RiskFactor> factors, DateTime assessmentDate)
        {
            // Use YearsInBusiness if available, otherwise calculate from OnboardingDate
            int years;
            string tenureSource;

            if (vendor.YearsInBusiness.HasValue)
            {
                years = vendor.YearsInBusiness.Value;
                tenureSource = "YearsInBusiness field";
            }
            else if (vendor.OnboardingDate.HasValue)
            {
                // Calculate years from onboarding date using the assessment date (not current date)
                var tenure = assessmentDate - vendor.OnboardingDate.Value;
                years = (int)(tenure.TotalDays / 365.25);
                tenureSource = $"calculated from OnboardingDate ({vendor.OnboardingDate.Value:yyyy-MM-dd})";
                _tracingService.Trace("YearsInBusiness not set - derived {0} years from OnboardingDate", years);
            }
            else
            {
                years = 0;
                tenureSource = "unknown (no data available)";
                _tracingService.Trace("WARNING: Neither YearsInBusiness nor OnboardingDate available - defaulting to 0 years");
            }

            var establishedYears = _config.TenureScores.Established?.Years ?? 10;
            var establishedScore = _config.TenureScores.Established?.Score ?? 100m;
            var matureYears = _config.TenureScores.Mature?.Years ?? 5;
            var matureScore = _config.TenureScores.Mature?.Score ?? 85m;
            var growingYears = _config.TenureScores.Growing?.Years ?? 2;
            var growingScore = _config.TenureScores.Growing?.Score ?? 65m;
            var newScore = _config.TenureScores.New?.Score ?? 40m;

            if (years >= establishedYears)
            {
                factors.Add(new RiskFactor
                {
                    Category = "Tenure",
                    Factor = "Years in Business",
                    Impact = "Positive",
                    ScoreImpact = establishedScore - 50m,
                    Details = $"Established business - {years} years"
                });
                return establishedScore;
            }
            else if (years >= matureYears)
            {
                factors.Add(new RiskFactor
                {
                    Category = "Tenure",
                    Factor = "Years in Business",
                    Impact = "Positive",
                    ScoreImpact = matureScore - 50m,
                    Details = $"Mature business - {years} years"
                });
                return matureScore;
            }
            else if (years >= growingYears)
            {
                factors.Add(new RiskFactor
                {
                    Category = "Tenure",
                    Factor = "Years in Business",
                    Impact = "Neutral",
                    ScoreImpact = growingScore - 50m,
                    Details = $"Growing business - {years} years"
                });
                return growingScore;
            }
            else
            {
                factors.Add(new RiskFactor
                {
                    Category = "Tenure",
                    Factor = "Years in Business",
                    Impact = "Negative",
                    ScoreImpact = newScore - 50m,
                    Details = $"New business - {years} years (higher risk)"
                });
                return newScore;
            }
        }

        private decimal CalculateDocumentationScore(ComplianceData compliance, List<RiskFactor> factors)
        {
            if (compliance.TotalDocuments == 0)
            {
                var deduction = _config.Deductions.NoDocuments ?? 0;
                factors.Add(new RiskFactor
                {
                    Category = "Documentation",
                    Factor = "No Documents",
                    Impact = "Negative",
                    ScoreImpact = -deduction,
                    Details = "No compliance documents on file"
                });
                return 100m - deduction;
            }

            var completenessRatio = (decimal)compliance.CurrentDocuments / Math.Max(compliance.TotalDocuments, 1);
            var score = completenessRatio * 100m;

            var impact = score >= 80 ? "Positive" : score >= 50 ? "Neutral" : "Negative";
            factors.Add(new RiskFactor
            {
                Category = "Documentation",
                Factor = "Completeness",
                Impact = impact,
                ScoreImpact = score - 70m,
                Details = $"{compliance.CurrentDocuments} of {compliance.TotalDocuments} documents current ({completenessRatio:P0})"
            });

            return score;
        }

        private string DetermineRiskCategory(decimal score, ComplianceRegistryData registry)
        {
            if (registry?.Debarred == true)
                return "Critical";

            if (score >= (_config.Thresholds.Low ?? 80m)) return "Low";
            if (score >= (_config.Thresholds.Medium ?? 60m)) return "Medium";
            if (score >= (_config.Thresholds.High ?? 40m)) return "High";
            return "Critical";
        }

        private DateTime CalculateNextReviewDate(string riskCategory, DateTime assessmentDate)
        {
            switch (riskCategory)
            {
                case "Critical":
                    return assessmentDate.AddDays(_config.ReviewDays.Critical ?? 7);
                case "High":
                    return assessmentDate.AddDays(_config.ReviewDays.High ?? 30);
                case "Medium":
                    return assessmentDate.AddDays(_config.ReviewDays.Medium ?? 90);
                case "Low":
                default:
                    return assessmentDate.AddDays(_config.ReviewDays.Low ?? 180);
            }
        }

        private PreviousAssessmentData GetPreviousAssessment(Guid vendorId, DateTime currentAssessmentDate)
        {
            try
            {
                // Query for the most recent assessment BEFORE the current assessment date
                var query = new QueryExpression("contoso_riskassessment")
                {
                    ColumnSet = new ColumnSet(
                        "contoso_riskassessmentid",
                        "contoso_riskscore",
                        "contoso_riskcategory",
                        "contoso_assessmentdate"
                    ),
                    Criteria = new FilterExpression
                    {
                        Conditions =
                        {
                            new ConditionExpression("contoso_vendorid", ConditionOperator.Equal, vendorId),
                            new ConditionExpression("contoso_assessmentdate", ConditionOperator.LessThan, currentAssessmentDate),
                            new ConditionExpression("statecode", ConditionOperator.Equal, 0) // Active records only
                        }
                    },
                    Orders =
                    {
                        new OrderExpression("contoso_assessmentdate", OrderType.Descending)
                    },
                    TopCount = 1
                };

                var results = _service.RetrieveMultiple(query);
                if (results.Entities.Count == 0)
                {
                    _tracingService.Trace("No previous assessment found for vendor {0} before {1}", vendorId, currentAssessmentDate);
                    return null;
                }

                var entity = results.Entities[0];
                var riskCategoryOptionSet = entity.GetAttributeValue<OptionSetValue>("contoso_riskcategory");
                var riskCategoryText = MapRiskCategoryOptionToString(riskCategoryOptionSet?.Value);

                return new PreviousAssessmentData
                {
                    AssessmentId = entity.Id,
                    RiskScore = entity.GetAttributeValue<decimal?>("contoso_riskscore") ?? 0,
                    RiskCategory = riskCategoryText,
                    AssessmentDate = entity.GetAttributeValue<DateTime?>("contoso_assessmentdate") ?? DateTime.MinValue
                };
            }
            catch (Exception ex)
            {
                _tracingService.Trace("Error retrieving previous assessment: {0}", ex.Message);
                return null;
            }
        }

        private string MapRiskCategoryOptionToString(int? optionValue)
        {
            if (!optionValue.HasValue) return "Unknown";

            // Map option set values to string categories
            // Adjust these values based on your actual contoso_riskcategory option set definition
            switch (optionValue.Value)
            {
                case 100000000: return "Low";
                case 100000001: return "Medium";
                case 100000002: return "High";
                case 100000003: return "Critical";
                default: return "Unknown";
            }
        }

        private List<Recommendation> GenerateRecommendations(
            List<RiskFactor> riskFactors,
            ComplianceData complianceData,
            ComplianceRegistryData registryData,
            ERPVendorData erpData)
        {
            var recommendations = new List<Recommendation>();

            // If vendor is debarred, only return the critical debarment recommendation
            // All other recommendations are irrelevant since business must cease immediately
            if (registryData?.Debarred == true)
            {
                recommendations.Add(new Recommendation
                {
                    Priority = "Critical",
                    Category = "Compliance",
                    Action = "Immediately suspend all business activities with this vendor and initiate offboarding",
                    Reason = "Vendor is debarred - continued engagement poses significant legal and compliance risk"
                });
                return recommendations;
            }

            foreach (var factor in riskFactors)
            {
                if (factor.Impact != "Negative") continue;

                switch (factor.Factor)
                {
                    case "Expired Documents":
                        recommendations.Add(new Recommendation
                        {
                            Priority = "High",
                            Category = "Compliance",
                            Action = "Request updated compliance documents immediately",
                            Reason = factor.Details
                        });
                        break;

                    case "Documents Expiring Soon":
                        recommendations.Add(new Recommendation
                        {
                            Priority = "Medium",
                            Category = "Compliance",
                            Action = "Proactively request document renewals before expiration",
                            Reason = factor.Details
                        });
                        break;

                    case "Debarred":
                        recommendations.Add(new Recommendation
                        {
                            Priority = "Critical",
                            Category = "Compliance",
                            Action = "Immediately suspend all business activities with this vendor and initiate offboarding",
                            Reason = "Vendor is debarred - continued engagement poses significant legal and compliance risk"
                        });
                        break;

                    case "SAM Status":
                        if (registryData?.SAMStatus?.Equals("Expired", StringComparison.OrdinalIgnoreCase) == true)
                        {
                            recommendations.Add(new Recommendation
                            {
                                Priority = "High",
                                Category = "Compliance",
                                Action = "Request vendor to renew SAM.gov registration immediately",
                                Reason = "SAM.gov registration has expired - required for federal contracts"
                            });
                        }
                        else
                        {
                            recommendations.Add(new Recommendation
                            {
                                Priority = "High",
                                Category = "Compliance",
                                Action = "Verify SAM.gov registration status and request activation",
                                Reason = factor.Details
                            });
                        }
                        break;

                    case "OSHA Violations":
                        recommendations.Add(new Recommendation
                        {
                            Priority = "Medium",
                            Category = "Safety",
                            Action = "Request vendor's corrective action plan for OSHA violations and proof of remediation",
                            Reason = factor.Details
                        });
                        break;

                    case "No ERP Data":
                        recommendations.Add(new Recommendation
                        {
                            Priority = "Low",
                            Category = "Data Quality",
                            Action = "Ensure vendor is properly set up in ERP system with accurate payment history",
                            Reason = "Missing ERP data prevents accurate payment behavior assessment"
                        });
                        break;

                    case "Missing Payment History":
                        recommendations.Add(new Recommendation
                        {
                            Priority = "Medium",
                            Category = "Data Quality",
                            Action = "Update ERP record with payment history data (AvgDaysToPay) for accurate risk assessment",
                            Reason = "ERP record exists but lacks payment behavior data needed for proper scoring"
                        });
                        break;

                    case "Payment Timeliness":
                        if (erpData?.AvgDaysToPay > 60)
                        {
                            recommendations.Add(new Recommendation
                            {
                                Priority = "High",
                                Category = "Financial",
                                Action = "Review payment terms with vendor and consider requiring prepayment or shorter terms",
                                Reason = factor.Details
                            });
                        }
                        else
                        {
                            recommendations.Add(new Recommendation
                            {
                                Priority = "Medium",
                                Category = "Financial",
                                Action = "Monitor payment patterns and consider adjusting payment terms if issues persist",
                                Reason = factor.Details
                            });
                        }
                        break;

                    case "ERP Status":
                        recommendations.Add(new Recommendation
                        {
                            Priority = "High",
                            Category = "Financial",
                            Action = "Investigate reason for vendor hold status in ERP and resolve before resuming orders",
                            Reason = "Vendor is currently on hold in ERP system"
                        });
                        break;

                    case "No Documents":
                        recommendations.Add(new Recommendation
                        {
                            Priority = "High",
                            Category = "Compliance",
                            Action = "Request all required compliance documents (insurance certificates, W-9, etc.)",
                            Reason = "No compliance documents on file - vendor cannot be properly validated"
                        });
                        break;

                    case "Completeness":
                        if (complianceData != null && complianceData.TotalDocuments > 0)
                        {
                            var ratio = (decimal)complianceData.CurrentDocuments / complianceData.TotalDocuments;
                            if (ratio < 0.5m)
                            {
                                recommendations.Add(new Recommendation
                                {
                                    Priority = "High",
                                    Category = "Compliance",
                                    Action = "Urgently request renewal of expired compliance documents",
                                    Reason = $"Less than 50% of documents are current ({complianceData.CurrentDocuments}/{complianceData.TotalDocuments})"
                                });
                            }
                        }
                        break;

                    case "Missing Vendor Number":
                        recommendations.Add(new Recommendation
                        {
                            Priority = "Medium",
                            Category = "Data Quality",
                            Action = "Add vendor number to enable ERP and compliance registry integration",
                            Reason = "Missing vendor number prevents retrieval of external compliance and payment data"
                        });
                        break;

                    case "Missing Expiration Dates":
                        recommendations.Add(new Recommendation
                        {
                            Priority = "Medium",
                            Category = "Compliance",
                            Action = "Update compliance documents to include expiration dates for proper tracking",
                            Reason = factor.Details
                        });
                        break;

                    case "Years in Business":
                        recommendations.Add(new Recommendation
                        {
                            Priority = "Low",
                            Category = "Risk Mitigation",
                            Action = "Consider additional due diligence for newer vendors including reference checks and smaller initial orders",
                            Reason = factor.Details
                        });
                        break;
                }
            }

            // Sort by priority (Critical > High > Medium > Low)
            recommendations.Sort((a, b) =>
            {
                var priorityOrder = new Dictionary<string, int>
                {
                    { "Critical", 0 },
                    { "High", 1 },
                    { "Medium", 2 },
                    { "Low", 3 }
                };
                var aOrder = priorityOrder.ContainsKey(a.Priority) ? priorityOrder[a.Priority] : 4;
                var bOrder = priorityOrder.ContainsKey(b.Priority) ? priorityOrder[b.Priority] : 4;
                return aOrder.CompareTo(bOrder);
            });

            return recommendations;
        }
    }
}
