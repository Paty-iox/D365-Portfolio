using System;
using Contoso.VendorRisk.Plugins.Models;
using Contoso.VendorRisk.Plugins.Services;
using Microsoft.Xrm.Sdk;
using Newtonsoft.Json;

namespace Contoso.VendorRisk.Plugins.Plugins
{
    public class CalculateVendorRiskScorePlugin : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            var tracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            var service = serviceFactory.CreateOrganizationService(context.UserId);

            tracingService.Trace("CalculateVendorRiskScorePlugin started");

            try
            {
                var input = new RiskCalculationInput
                {
                    VendorId = GetVendorId(context),
                    AssessmentDate = GetAssessmentDate(context),
                    IncludeHistoricalTrend = GetIncludeHistoricalTrend(context)
                };

                tracingService.Trace("Input - VendorId: {0}, Date: {1}, IncludeHistory: {2}",
                    input.VendorId, input.AssessmentDate, input.IncludeHistoricalTrend);

                var calculator = new RiskCalculationService(service, tracingService);
                var result = calculator.CalculateRisk(input);

                // Core risk assessment outputs
                context.OutputParameters["RiskScore"] = result.RiskScore;
                context.OutputParameters["RiskCategory"] = result.RiskCategory;
                context.OutputParameters["RiskFactors"] = JsonConvert.SerializeObject(result.RiskFactors);
                context.OutputParameters["NextReviewDate"] = result.NextReviewDate;

                // Component scores
                context.OutputParameters["ComplianceScore"] = result.ComplianceScore;
                context.OutputParameters["PaymentScore"] = result.PaymentScore;
                context.OutputParameters["TenureScore"] = result.TenureScore;
                context.OutputParameters["DocumentationScore"] = result.DocumentationScore;

                // Previous assessment comparison (use default values if no previous assessment exists)
                context.OutputParameters["PreviousRiskScore"] = result.PreviousRiskScore ?? 0m;
                context.OutputParameters["PreviousRiskCategory"] = result.PreviousRiskCategory ?? string.Empty;
                context.OutputParameters["ScoreChange"] = result.ScoreChange ?? 0m;

                // Recommendations
                context.OutputParameters["Recommendations"] = JsonConvert.SerializeObject(result.Recommendations);

                // Configuration observability
                context.OutputParameters["ConfigSource"] = result.ConfigSource ?? string.Empty;
                context.OutputParameters["ConfigWarning"] = result.ConfigWarning ?? string.Empty;

                tracingService.Trace("CalculateVendorRiskScorePlugin completed successfully - Score: {0}, Category: {1}, Recommendations: {2}, ConfigSource: {3}",
                    result.RiskScore, result.RiskCategory, result.Recommendations.Count, result.ConfigSource);
            }
            catch (InvalidPluginExecutionException)
            {
                throw;
            }
            catch (Exception ex)
            {
                tracingService.Trace("Error: {0}", ex.ToString());
                throw new InvalidPluginExecutionException($"Error calculating risk score: {ex.Message}", ex);
            }
        }

        private Guid GetVendorId(IPluginExecutionContext context)
        {
            if (!context.InputParameters.Contains("VendorId"))
                throw new InvalidPluginExecutionException("VendorId is required.");

            var value = context.InputParameters["VendorId"];
            if (value == null)
                throw new InvalidPluginExecutionException("VendorId cannot be null.");

            if (value is Guid guidValue)
                return guidValue;

            if (value is string stringValue)
            {
                if (Guid.TryParse(stringValue, out var parsedGuid))
                    return parsedGuid;
                throw new InvalidPluginExecutionException($"VendorId '{stringValue}' is not a valid GUID format.");
            }

            throw new InvalidPluginExecutionException($"VendorId must be a GUID. Received type: {value.GetType().Name}");
        }

        private DateTime GetAssessmentDate(IPluginExecutionContext context)
        {
            if (!context.InputParameters.Contains("AssessmentDate"))
                return DateTime.UtcNow;

            var value = context.InputParameters["AssessmentDate"];
            if (value == null)
                return DateTime.UtcNow;

            if (value is DateTime dateValue)
            {
                if (dateValue == DateTime.MinValue || dateValue.Year < 2000)
                    return DateTime.UtcNow;
                return dateValue;
            }

            if (value is string stringValue)
            {
                if (DateTime.TryParse(stringValue, out var parsedDate) && parsedDate.Year >= 2000)
                    return parsedDate;
                return DateTime.UtcNow;
            }

            return DateTime.UtcNow;
        }

        private bool GetIncludeHistoricalTrend(IPluginExecutionContext context)
        {
            if (!context.InputParameters.Contains("IncludeHistoricalTrend"))
                return false;

            var value = context.InputParameters["IncludeHistoricalTrend"];
            if (value == null)
                return false;

            if (value is bool boolValue)
                return boolValue;

            if (value is string stringValue)
                return stringValue.Equals("true", StringComparison.OrdinalIgnoreCase);

            return false;
        }
    }
}
