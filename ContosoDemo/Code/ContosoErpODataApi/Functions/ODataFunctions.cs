using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using ContosoErpODataApi.Models;
using ContosoErpODataApi.Services;

namespace ContosoErpODataApi.Functions;

/// <summary>
/// OData v4 for Dataverse virtual tables. Vendor IDs are ints in ERP but exposed as GUIDs
/// (see IntToGuid/GuidToInt). Example: 1 => 00000001-0000-0000-0000-000000000000.
/// </summary>
public class ODataFunctions
{
    private readonly ILogger<ODataFunctions> _logger;
    private readonly ISqlDataService _dataService;
    private readonly IComplianceDataService _complianceService;

    private enum ColumnType { String, Int, Decimal, DateTimeOffset, Guid, Bool }

    private static readonly Dictionary<string, (string SqlName, ColumnType Type)> VendorColumnMetadata =
        new(StringComparer.OrdinalIgnoreCase)
    {
        ["vendor_id"] = ("vendor_id", ColumnType.Guid),
        ["vendor_number"] = ("vendor_number", ColumnType.String),
        ["company_name"] = ("company_name", ColumnType.String),
        ["payment_terms"] = ("payment_terms", ColumnType.String),
        ["credit_limit"] = ("credit_limit", ColumnType.Decimal),
        ["ytd_purchases"] = ("ytd_purchases", ColumnType.Decimal),
        ["open_po_count"] = ("open_po_count", ColumnType.Int),
        ["open_po_value"] = ("open_po_value", ColumnType.Decimal),
        ["last_payment_date"] = ("last_payment_date", ColumnType.DateTimeOffset),
        ["last_payment_amount"] = ("last_payment_amount", ColumnType.Decimal),
        ["average_days_to_pay"] = ("average_days_to_pay", ColumnType.Int),
        ["vendor_since"] = ("vendor_since", ColumnType.DateTimeOffset),
        ["vendor_status"] = ("vendor_status", ColumnType.String),
        ["currency_code"] = ("currency_code", ColumnType.String),
        ["tax_id"] = ("tax_id", ColumnType.String),
        ["duns_number"] = ("duns_number", ColumnType.String),
        ["primary_contact"] = ("primary_contact", ColumnType.String),
        ["contact_email"] = ("contact_email", ColumnType.String),
        ["contact_phone"] = ("contact_phone", ColumnType.String)
    };

    private static readonly Dictionary<string, (string SqlName, ColumnType Type)> ComplianceColumnMetadata =
        new(StringComparer.OrdinalIgnoreCase)
    {
        ["registry_id"] = ("registry_id", ColumnType.Guid),
        ["vendor_number"] = ("vendor_number", ColumnType.String),
        ["sam_status"] = ("sam_status", ColumnType.String),
        ["sam_expiry"] = ("sam_expiry", ColumnType.DateTimeOffset),
        ["osha_violation_count"] = ("osha_violation_count", ColumnType.Int),
        ["osha_last_inspection"] = ("osha_last_inspection", ColumnType.DateTimeOffset),
        ["debarred"] = ("debarred", ColumnType.Bool)
    };

    // Whitelist of allowed OData operators
    private static readonly Dictionary<string, string> AllowedOperators = new(StringComparer.OrdinalIgnoreCase)
    {
        ["eq"] = "=",
        ["ne"] = "<>",
        ["gt"] = ">",
        ["ge"] = ">=",
        ["lt"] = "<",
        ["le"] = "<="
    };

    private const int MaxTop = 1000;
    private const int DefaultTop = 100;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
    };

    // TODO: $select support for column projection
    // TODO: $orderby support for custom sorting
    // TODO: Parentheses in $filter (e.g., (A or B) and C)

    public ODataFunctions(ILogger<ODataFunctions> logger, ISqlDataService dataService, IComplianceDataService complianceService)
    {
        _logger = logger;
        _dataService = dataService;
        _complianceService = complianceService;
    }

    // GET /api/odata/$metadata
    [Function("GetMetadata")]
    public async Task<HttpResponseData> GetMetadata(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "odata/$metadata")] HttpRequestData req)
    {
        _logger.LogInformation("OData $metadata requested");

        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/xml");

        var metadataXml = GenerateMetadataXml();
        await response.WriteStringAsync(metadataXml);

        return response;
    }

    // GET /api/odata/VendorMaster
    [Function("GetVendors")]
    public async Task<HttpResponseData> GetVendors(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "odata/VendorMaster")] HttpRequestData req)
    {
        _logger.LogInformation("OData VendorMaster collection requested");

        try
        {
            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);

            int top;
            if (!string.IsNullOrEmpty(query["$top"]))
            {
                if (!int.TryParse(query["$top"], out var topValue) || topValue < 0)
                {
                    return await CreateBadRequestResponse(req, "Invalid $top value. Must be a non-negative integer.");
                }
                top = Math.Min(topValue, MaxTop); // Cap at max
            }
            else
            {
                top = DefaultTop; // Apply default to prevent full table scans
            }

            int? skip = null;
            if (!string.IsNullOrEmpty(query["$skip"]))
            {
                if (!int.TryParse(query["$skip"], out var skipValue) || skipValue < 0)
                {
                    return await CreateBadRequestResponse(req, "Invalid $skip value. Must be a non-negative integer.");
                }
                skip = skipValue;
            }

            var includeCount = string.Equals(query["$count"], "true", StringComparison.OrdinalIgnoreCase);

            ParsedFilter? filter = null;
            if (!string.IsNullOrEmpty(query["$filter"]))
            {
                var parseResult = ParseODataFilter(query["$filter"], VendorColumnMetadata);
                if (!parseResult.Success)
                {
                    return await CreateBadRequestResponse(req, parseResult.ErrorMessage ?? "Invalid filter");
                }
                filter = parseResult.Filter;
            }

            var vendors = await _dataService.GetVendorsAsync(filter, top, skip);

            int? count = null;
            if (includeCount)
            {
                count = await _dataService.GetCountAsync(filter);
            }

            // Convert vendors to response format with GUID vendor_id
            var vendorValues = vendors.Select(v => {
                var vendorGuid = IntToGuid(v.vendor_id);
                var entity = new Dictionary<string, object?>
                {
                    ["@odata.type"] = "#ContosoErp.VendorMaster",
                    ["@odata.id"] = $"VendorMaster({vendorGuid})",
                    ["vendor_id"] = vendorGuid,
                    ["vendor_number"] = v.vendor_number,
                    ["company_name"] = v.company_name,
                    ["payment_terms"] = v.payment_terms,
                    ["credit_limit"] = v.credit_limit,
                    ["ytd_purchases"] = v.ytd_purchases,
                    ["open_po_count"] = v.open_po_count,
                    ["open_po_value"] = v.open_po_value,
                    ["last_payment_date"] = v.last_payment_date,
                    ["last_payment_amount"] = v.last_payment_amount,
                    ["average_days_to_pay"] = v.average_days_to_pay,
                    ["vendor_since"] = v.vendor_since,
                    ["vendor_status"] = v.vendor_status,
                    ["currency_code"] = v.currency_code,
                    ["tax_id"] = v.tax_id,
                    ["duns_number"] = v.duns_number,
                    ["primary_contact"] = v.primary_contact,
                    ["contact_email"] = v.contact_email,
                    ["contact_phone"] = v.contact_phone
                };
                return entity;
            }).ToList();

            var result = new Dictionary<string, object>
            {
                ["@odata.context"] = $"{GetBaseUrl(req)}/$metadata#VendorMaster",
                ["value"] = vendorValues
            };

            if (count.HasValue)
            {
                result["@odata.count"] = count.Value;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/json; odata.metadata=minimal");

            await response.WriteStringAsync(JsonSerializer.Serialize(result, JsonOptions));

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetVendors");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteStringAsync("An error occurred processing your request.");
            return errorResponse;
        }
    }

    private async Task<HttpResponseData> CreateBadRequestResponse(HttpRequestData req, string message)
    {
        var response = req.CreateResponse(HttpStatusCode.BadRequest);
        await response.WriteStringAsync(message);
        return response;
    }

    // GET /api/odata/VendorMaster({id})
    [Function("GetVendorById")]
    public async Task<HttpResponseData> GetVendorById(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "odata/VendorMaster({id})")] HttpRequestData req,
        string id)
    {
        _logger.LogInformation("OData VendorMaster({Id}) requested", id);

        if (!Guid.TryParse(id, out var vendorGuid))
        {
            _logger.LogWarning("Invalid vendor ID format: {Id}", id);
            var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
            await badRequest.WriteStringAsync("Invalid vendor ID format");
            return badRequest;
        }

        int vendorId;
        try
        {
            vendorId = GuidToInt(vendorGuid);
        }
        catch (ArgumentException)
        {
            _logger.LogWarning("Invalid GUID pattern: {Id}", id);
            var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
            await badRequest.WriteStringAsync("Invalid vendor ID format");
            return badRequest;
        }
        _logger.LogInformation("Converted GUID {Guid} to vendor_id {VendorId}", vendorGuid, vendorId);

        var vendor = await _dataService.GetVendorByIdAsync(vendorId);

        if (vendor == null)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            return notFound;
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json; odata.metadata=minimal");

        var result = new Dictionary<string, object?>
        {
            ["@odata.context"] = $"{GetBaseUrl(req)}/$metadata#VendorMaster/$entity",
            ["@odata.type"] = "#ContosoErp.VendorMaster",
            ["@odata.id"] = $"VendorMaster({vendorGuid})",
            ["vendor_id"] = vendorGuid,
            ["vendor_number"] = vendor.vendor_number,
            ["company_name"] = vendor.company_name,
            ["payment_terms"] = vendor.payment_terms,
            ["credit_limit"] = vendor.credit_limit,
            ["ytd_purchases"] = vendor.ytd_purchases,
            ["open_po_count"] = vendor.open_po_count,
            ["open_po_value"] = vendor.open_po_value,
            ["last_payment_date"] = vendor.last_payment_date,
            ["last_payment_amount"] = vendor.last_payment_amount,
            ["average_days_to_pay"] = vendor.average_days_to_pay,
            ["vendor_since"] = vendor.vendor_since,
            ["vendor_status"] = vendor.vendor_status,
            ["currency_code"] = vendor.currency_code,
            ["tax_id"] = vendor.tax_id,
            ["duns_number"] = vendor.duns_number,
            ["primary_contact"] = vendor.primary_contact,
            ["contact_email"] = vendor.contact_email,
            ["contact_phone"] = vendor.contact_phone
        };

        await response.WriteStringAsync(JsonSerializer.Serialize(result, JsonOptions));

        return response;
    }

    // GET /api/odata/ComplianceRegistry
    [Function("GetComplianceRecords")]
    public async Task<HttpResponseData> GetComplianceRecords(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "odata/ComplianceRegistry")] HttpRequestData req)
    {
        _logger.LogInformation("OData ComplianceRegistry collection requested");

        try
        {
            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);

            int top = DefaultTop;
            if (!string.IsNullOrEmpty(query["$top"]))
            {
                if (!int.TryParse(query["$top"], out var topValue) || topValue < 0)
                {
                    return await CreateBadRequestResponse(req, "Invalid $top value. Must be a non-negative integer.");
                }
                top = Math.Min(topValue, MaxTop);
            }

            int? skip = null;
            if (!string.IsNullOrEmpty(query["$skip"]))
            {
                if (!int.TryParse(query["$skip"], out var skipValue) || skipValue < 0)
                {
                    return await CreateBadRequestResponse(req, "Invalid $skip value. Must be a non-negative integer.");
                }
                skip = skipValue;
            }

            var includeCount = string.Equals(query["$count"], "true", StringComparison.OrdinalIgnoreCase);

            Func<ComplianceRegistry, bool>? filterFunc = null;
            if (!string.IsNullOrEmpty(query["$filter"]))
            {
                filterFunc = BuildComplianceFilter(query["$filter"]);
                if (filterFunc == null)
                {
                    return await CreateBadRequestResponse(req, "Invalid filter expression");
                }
            }

            var records = await _complianceService.GetComplianceRecordsAsync(filterFunc, top, skip);

            int? count = null;
            if (includeCount)
            {
                count = await _complianceService.GetCountAsync(filterFunc);
            }

            var values = records.Select(c => {
                var registryGuid = IntToGuid(c.registry_id);
                return new Dictionary<string, object?>
                {
                    ["@odata.type"] = "#ContosoErp.ComplianceRegistry",
                    ["@odata.id"] = $"ComplianceRegistry({registryGuid})",
                    ["registry_id"] = registryGuid,
                    ["vendor_number"] = c.vendor_number,
                    ["sam_status"] = c.sam_status,
                    ["sam_expiry"] = c.sam_expiry,
                    ["osha_violation_count"] = c.osha_violation_count,
                    ["osha_last_inspection"] = c.osha_last_inspection,
                    ["debarred"] = c.debarred
                };
            }).ToList();

            var result = new Dictionary<string, object>
            {
                ["@odata.context"] = $"{GetBaseUrl(req)}/$metadata#ComplianceRegistry",
                ["value"] = values
            };

            if (count.HasValue)
            {
                result["@odata.count"] = count.Value;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/json; odata.metadata=minimal");

            await response.WriteStringAsync(JsonSerializer.Serialize(result, JsonOptions));

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetComplianceRecords");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteStringAsync("An error occurred processing your request.");
            return errorResponse;
        }
    }

    // GET /api/odata/ComplianceRegistry({id})
    [Function("GetComplianceById")]
    public async Task<HttpResponseData> GetComplianceById(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "odata/ComplianceRegistry({id})")] HttpRequestData req,
        string id)
    {
        _logger.LogInformation("OData ComplianceRegistry({Id}) requested", id);

        if (!Guid.TryParse(id, out var registryGuid))
        {
            var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
            await badRequest.WriteStringAsync("Invalid registry ID format");
            return badRequest;
        }

        int registryId;
        try
        {
            registryId = GuidToInt(registryGuid);
        }
        catch (ArgumentException)
        {
            var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
            await badRequest.WriteStringAsync("Invalid registry ID format");
            return badRequest;
        }
        var record = await _complianceService.GetComplianceByIdAsync(registryId);

        if (record == null)
        {
            return req.CreateResponse(HttpStatusCode.NotFound);
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json; odata.metadata=minimal");

        var result = new Dictionary<string, object?>
        {
            ["@odata.context"] = $"{GetBaseUrl(req)}/$metadata#ComplianceRegistry/$entity",
            ["@odata.type"] = "#ContosoErp.ComplianceRegistry",
            ["@odata.id"] = $"ComplianceRegistry({registryGuid})",
            ["registry_id"] = registryGuid,
            ["vendor_number"] = record.vendor_number,
            ["sam_status"] = record.sam_status,
            ["sam_expiry"] = record.sam_expiry,
            ["osha_violation_count"] = record.osha_violation_count,
            ["osha_last_inspection"] = record.osha_last_inspection,
            ["debarred"] = record.debarred
        };

        await response.WriteStringAsync(JsonSerializer.Serialize(result, JsonOptions));

        return response;
    }

    private static string GenerateMetadataXml()
    {
        return @"<?xml version=""1.0"" encoding=""utf-8""?>
<edmx:Edmx Version=""4.0"" xmlns:edmx=""http://docs.oasis-open.org/odata/ns/edmx"">
  <edmx:DataServices>
    <Schema Namespace=""ContosoErp"" xmlns=""http://docs.oasis-open.org/odata/ns/edm"">
      <EntityType Name=""VendorMaster"">
        <Key>
          <PropertyRef Name=""vendor_id""/>
        </Key>
        <Property Name=""vendor_id"" Type=""Edm.Guid"" Nullable=""false""/>
        <Property Name=""vendor_number"" Type=""Edm.String"" MaxLength=""20"" Nullable=""false""/>
        <Property Name=""company_name"" Type=""Edm.String"" MaxLength=""200"" Nullable=""false""/>
        <Property Name=""payment_terms"" Type=""Edm.String"" MaxLength=""20"" Nullable=""false""/>
        <Property Name=""credit_limit"" Type=""Edm.Decimal"" Precision=""18"" Scale=""2"" Nullable=""true""/>
        <Property Name=""ytd_purchases"" Type=""Edm.Decimal"" Precision=""18"" Scale=""2"" Nullable=""true""/>
        <Property Name=""open_po_count"" Type=""Edm.Int32"" Nullable=""true""/>
        <Property Name=""open_po_value"" Type=""Edm.Decimal"" Precision=""18"" Scale=""2"" Nullable=""true""/>
        <Property Name=""last_payment_date"" Type=""Edm.DateTimeOffset"" Nullable=""true""/>
        <Property Name=""last_payment_amount"" Type=""Edm.Decimal"" Precision=""18"" Scale=""2"" Nullable=""true""/>
        <Property Name=""average_days_to_pay"" Type=""Edm.Int32"" Nullable=""true""/>
        <Property Name=""vendor_since"" Type=""Edm.DateTimeOffset"" Nullable=""true""/>
        <Property Name=""vendor_status"" Type=""Edm.String"" MaxLength=""20"" Nullable=""true""/>
        <Property Name=""currency_code"" Type=""Edm.String"" MaxLength=""3"" Nullable=""true""/>
        <Property Name=""tax_id"" Type=""Edm.String"" MaxLength=""20"" Nullable=""true""/>
        <Property Name=""duns_number"" Type=""Edm.String"" MaxLength=""15"" Nullable=""true""/>
        <Property Name=""primary_contact"" Type=""Edm.String"" MaxLength=""100"" Nullable=""true""/>
        <Property Name=""contact_email"" Type=""Edm.String"" MaxLength=""100"" Nullable=""true""/>
        <Property Name=""contact_phone"" Type=""Edm.String"" MaxLength=""20"" Nullable=""true""/>
      </EntityType>
      <EntityType Name=""ComplianceRegistry"">
        <Key>
          <PropertyRef Name=""registry_id""/>
        </Key>
        <Property Name=""registry_id"" Type=""Edm.Guid"" Nullable=""false""/>
        <Property Name=""vendor_number"" Type=""Edm.String"" MaxLength=""20"" Nullable=""false""/>
        <Property Name=""sam_status"" Type=""Edm.String"" MaxLength=""20"" Nullable=""true""/>
        <Property Name=""sam_expiry"" Type=""Edm.DateTimeOffset"" Nullable=""true""/>
        <Property Name=""osha_violation_count"" Type=""Edm.Int32"" Nullable=""true""/>
        <Property Name=""osha_last_inspection"" Type=""Edm.DateTimeOffset"" Nullable=""true""/>
        <Property Name=""debarred"" Type=""Edm.Boolean"" Nullable=""true""/>
      </EntityType>
      <EntityContainer Name=""Container"">
        <EntitySet Name=""VendorMaster"" EntityType=""ContosoErp.VendorMaster""/>
        <EntitySet Name=""ComplianceRegistry"" EntityType=""ContosoErp.ComplianceRegistry""/>
      </EntityContainer>
    </Schema>
  </edmx:DataServices>
</edmx:Edmx>";
    }

    private static string GetBaseUrl(HttpRequestData req)
    {
        var uri = req.Url;
        return $"{uri.Scheme}://{uri.Host}{(uri.Port != 80 && uri.Port != 443 ? $":{uri.Port}" : "")}/api/odata";
    }

    private class FilterParseResult
    {
        public bool Success { get; set; }
        public ParsedFilter? Filter { get; set; }
        public string? ErrorMessage { get; set; }

        public static FilterParseResult Ok(ParsedFilter filter) => new() { Success = true, Filter = filter };
        public static FilterParseResult Error(string message) => new() { Success = false, ErrorMessage = message };
    }

    // Split filter into tokens, preserving quoted strings
    private static List<string> TokenizeFilter(string filter)
    {
        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuote = false;

        for (int i = 0; i < filter.Length; i++)
        {
            char c = filter[i];

            if (c == '\'' && !inQuote)
            {
                inQuote = true;
                current.Append(c);
            }
            else if (c == '\'' && inQuote)
            {
                // Check for escaped quote ('')
                if (i + 1 < filter.Length && filter[i + 1] == '\'')
                {
                    current.Append("''");
                    i++; // Skip next quote
                }
                else
                {
                    inQuote = false;
                    current.Append(c);
                }
            }
            else if (char.IsWhiteSpace(c) && !inQuote)
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }

    // Convert OData $filter to parameterized SQL WHERE clause
    private FilterParseResult ParseODataFilter(string odataFilter, Dictionary<string, (string SqlName, ColumnType Type)> columnMetadata)
    {
        var filter = new ParsedFilter();
        var sqlParts = new List<string>();
        var paramIndex = 0;

        var tokens = TokenizeFilter(odataFilter);
        var currentConditionTokens = new List<string>();

        foreach (var token in tokens)
        {
            if (string.Equals(token, "and", StringComparison.OrdinalIgnoreCase) && currentConditionTokens.Count > 0)
            {
                var condition = string.Join(" ", currentConditionTokens);
                var result = ParseCondition(condition, ref paramIndex, filter.Parameters, columnMetadata);
                if (!result.Success)
                {
                    return FilterParseResult.Error(result.ErrorMessage ?? "Invalid filter condition");
                }
                sqlParts.Add(result.SqlCondition!);
                sqlParts.Add("AND");
                currentConditionTokens.Clear();
            }
            else if (string.Equals(token, "or", StringComparison.OrdinalIgnoreCase) && currentConditionTokens.Count > 0)
            {
                var condition = string.Join(" ", currentConditionTokens);
                var result = ParseCondition(condition, ref paramIndex, filter.Parameters, columnMetadata);
                if (!result.Success)
                {
                    return FilterParseResult.Error(result.ErrorMessage ?? "Invalid filter condition");
                }
                sqlParts.Add(result.SqlCondition!);
                sqlParts.Add("OR");
                currentConditionTokens.Clear();
            }
            else
            {
                currentConditionTokens.Add(token);
            }
        }

        if (currentConditionTokens.Count > 0)
        {
            var condition = string.Join(" ", currentConditionTokens);
            var result = ParseCondition(condition, ref paramIndex, filter.Parameters, columnMetadata);
            if (!result.Success)
            {
                return FilterParseResult.Error(result.ErrorMessage ?? "Invalid filter condition");
            }
            sqlParts.Add(result.SqlCondition!);
        }

        filter.WhereClause = string.Join(" ", sqlParts);
        return FilterParseResult.Ok(filter);
    }

    private class ConditionParseResult
    {
        public bool Success { get; set; }
        public string? SqlCondition { get; set; }
        public string? ErrorMessage { get; set; }
    }

    private ConditionParseResult ParseCondition(string condition, ref int paramIndex, List<SqlParameter> parameters,
        Dictionary<string, (string SqlName, ColumnType Type)> columnMetadata)
    {
        // contains(), startswith(), endswith()
        var funcMatch = Regex.Match(condition,
            @"^(contains|startswith|endswith)\((\w+),\s*'([^']*)'\)$",
            RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));

        if (funcMatch.Success)
        {
            return ParseFunctionCondition(funcMatch, ref paramIndex, parameters, columnMetadata);
        }

        var match = Regex.Match(condition,
            @"^(\w+)\s+(eq|ne|gt|ge|lt|le)\s+(.+)$",
            RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));

        if (!match.Success)
        {
            return new ConditionParseResult
            {
                Success = false,
                ErrorMessage = $"Invalid condition format: {condition}"
            };
        }

        var column = match.Groups[1].Value;
        var op = match.Groups[2].Value.ToLower();
        var valueStr = match.Groups[3].Value.Trim();

        if (!columnMetadata.TryGetValue(column, out var columnInfo))
        {
            return new ConditionParseResult
            {
                Success = false,
                ErrorMessage = $"Column '{column}' is not allowed in filters"
            };
        }

        var sqlColumn = columnInfo.SqlName;
        var expectedType = columnInfo.Type;

        if (!AllowedOperators.TryGetValue(op, out var sqlOp))
        {
            return new ConditionParseResult
            {
                Success = false,
                ErrorMessage = $"Operator '{op}' is not allowed"
            };
        }

        var paramName = $"@p{paramIndex++}";
        object? paramValue;

        if (string.Equals(valueStr, "null", StringComparison.OrdinalIgnoreCase))
        {
            var nullOp = sqlOp == "=" ? "IS" : "IS NOT";
            return new ConditionParseResult
            {
                Success = true,
                SqlCondition = $"{sqlColumn} {nullOp} NULL"
            };
        }

        switch (expectedType)
        {
            case ColumnType.String:
                if (!valueStr.StartsWith("'") || !valueStr.EndsWith("'"))
                {
                    return new ConditionParseResult
                    {
                        Success = false,
                        ErrorMessage = $"Column '{column}' expects a string value (e.g., 'value')"
                    };
                }
                paramValue = valueStr.Substring(1, valueStr.Length - 2).Replace("''", "'");
                break;

            case ColumnType.Int:
                if (!int.TryParse(valueStr, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out var intValue))
                {
                    return new ConditionParseResult
                    {
                        Success = false,
                        ErrorMessage = $"Column '{column}' expects an integer value"
                    };
                }
                paramValue = intValue;
                break;

            case ColumnType.Decimal:
                var decimalStr = valueStr;
                if (decimalStr.EndsWith("M", StringComparison.OrdinalIgnoreCase))
                {
                    decimalStr = decimalStr.Substring(0, decimalStr.Length - 1);
                }
                if (!decimal.TryParse(decimalStr, System.Globalization.NumberStyles.Number,
                    System.Globalization.CultureInfo.InvariantCulture, out var decimalValue))
                {
                    return new ConditionParseResult
                    {
                        Success = false,
                        ErrorMessage = $"Column '{column}' expects a decimal value (e.g., 123.45 or 123.45M)"
                    };
                }
                paramValue = decimalValue;
                break;

            case ColumnType.DateTimeOffset:
                var dateStr = valueStr;
                if (dateStr.StartsWith("datetime'", StringComparison.OrdinalIgnoreCase) && dateStr.EndsWith("'"))
                {
                    dateStr = dateStr.Substring(9, dateStr.Length - 10);
                }
                if (!DateTimeOffset.TryParse(dateStr, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var dateValue))
                {
                    return new ConditionParseResult
                    {
                        Success = false,
                        ErrorMessage = $"Column '{column}' expects a date/time value (e.g., 2024-01-15T00:00:00Z)"
                    };
                }
                paramValue = dateValue;
                break;

            case ColumnType.Guid:
                var guidStr = valueStr;
                if (guidStr.StartsWith("guid'", StringComparison.OrdinalIgnoreCase) && guidStr.EndsWith("'"))
                {
                    guidStr = guidStr.Substring(5, guidStr.Length - 6);
                }
                if (!Guid.TryParse(guidStr, out var guidValue))
                {
                    return new ConditionParseResult
                    {
                        Success = false,
                        ErrorMessage = $"Column '{column}' expects a GUID value (e.g., guid'00000001-0000-0000-0000-000000000000')"
                    };
                }
                paramValue = GuidToInt(guidValue);
                break;

            case ColumnType.Bool:
                if (string.Equals(valueStr, "true", StringComparison.OrdinalIgnoreCase))
                {
                    paramValue = true;
                }
                else if (string.Equals(valueStr, "false", StringComparison.OrdinalIgnoreCase))
                {
                    paramValue = false;
                }
                else
                {
                    return new ConditionParseResult
                    {
                        Success = false,
                        ErrorMessage = $"Column '{column}' expects a boolean value (true or false)"
                    };
                }
                break;

            default:
                return new ConditionParseResult
                {
                    Success = false,
                    ErrorMessage = $"Unknown column type for '{column}'"
                };
        }

        parameters.Add(new SqlParameter(paramName, paramValue));

        return new ConditionParseResult
        {
            Success = true,
            SqlCondition = $"{sqlColumn} {sqlOp} {paramName}"
        };
    }

    // Convert string functions to SQL LIKE
    private ConditionParseResult ParseFunctionCondition(Match funcMatch, ref int paramIndex, List<SqlParameter> parameters,
        Dictionary<string, (string SqlName, ColumnType Type)> columnMetadata)
    {
        var funcName = funcMatch.Groups[1].Value.ToLower();
        var column = funcMatch.Groups[2].Value;
        var searchValue = funcMatch.Groups[3].Value;

        if (!columnMetadata.TryGetValue(column, out var columnInfo))
        {
            return new ConditionParseResult
            {
                Success = false,
                ErrorMessage = $"Column '{column}' is not allowed in filters"
            };
        }

        if (columnInfo.Type != ColumnType.String)
        {
            return new ConditionParseResult
            {
                Success = false,
                ErrorMessage = $"Function '{funcName}' can only be used on string columns"
            };
        }

        var sqlColumn = columnInfo.SqlName;
        var paramName = $"@p{paramIndex++}";

        var escapedValue = searchValue
            .Replace(@"\", @"\\")
            .Replace("%", @"\%")
            .Replace("_", @"\_");

        string likePattern = funcName switch
        {
            "contains" => $"%{escapedValue}%",
            "startswith" => $"{escapedValue}%",
            "endswith" => $"%{escapedValue}",
            _ => $"%{escapedValue}%"
        };

        parameters.Add(new SqlParameter(paramName, likePattern));

        return new ConditionParseResult
        {
            Success = true,
            SqlCondition = $"{sqlColumn} LIKE {paramName} ESCAPE '\\'"
        };
    }

    private Func<ComplianceRegistry, bool>? BuildComplianceFilter(string filterString)
    {
        var tokens = TokenizeFilter(filterString);
        var conditions = new List<(Func<ComplianceRegistry, bool> predicate, string connector)>();
        var currentTokens = new List<string>();
        string? lastConnector = null;

        foreach (var token in tokens)
        {
            if (string.Equals(token, "and", StringComparison.OrdinalIgnoreCase) && currentTokens.Count > 0)
            {
                var condition = string.Join(" ", currentTokens);
                var predicate = ParseComplianceCondition(condition);
                if (predicate == null) return null;
                conditions.Add((predicate, lastConnector ?? "and"));
                lastConnector = "and";
                currentTokens.Clear();
            }
            else if (string.Equals(token, "or", StringComparison.OrdinalIgnoreCase) && currentTokens.Count > 0)
            {
                var condition = string.Join(" ", currentTokens);
                var predicate = ParseComplianceCondition(condition);
                if (predicate == null) return null;
                conditions.Add((predicate, lastConnector ?? "and"));
                lastConnector = "or";
                currentTokens.Clear();
            }
            else
            {
                currentTokens.Add(token);
            }
        }

        if (currentTokens.Count > 0)
        {
            var condition = string.Join(" ", currentTokens);
            var predicate = ParseComplianceCondition(condition);
            if (predicate == null) return null;
            conditions.Add((predicate, lastConnector ?? "and"));
        }

        if (conditions.Count == 0) return null;

        return c =>
        {
            bool result = conditions[0].predicate(c);
            for (int i = 1; i < conditions.Count; i++)
            {
                if (conditions[i].connector == "or")
                    result = result || conditions[i].predicate(c);
                else
                    result = result && conditions[i].predicate(c);
            }
            return result;
        };
    }

    private Func<ComplianceRegistry, bool>? ParseComplianceCondition(string condition)
    {
        var match = Regex.Match(condition, @"^(\w+)\s+(eq|ne|gt|ge|lt|le)\s+(.+)$", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));
        if (!match.Success) return null;

        var column = match.Groups[1].Value.ToLower();
        var op = match.Groups[2].Value.ToLower();
        var valueStr = match.Groups[3].Value.Trim();

        return column switch
        {
            "vendor_number" => BuildStringPredicate(c => c.vendor_number, op, valueStr),
            "sam_status" => BuildStringPredicate(c => c.sam_status, op, valueStr),
            "osha_violation_count" => BuildIntPredicate(c => c.osha_violation_count, op, valueStr),
            "debarred" => BuildBoolPredicate(c => c.debarred, op, valueStr),
            _ => null
        };
    }

    private Func<ComplianceRegistry, bool>? BuildStringPredicate(Func<ComplianceRegistry, string?> getter, string op, string valueStr)
    {
        if (!valueStr.StartsWith("'") || !valueStr.EndsWith("'")) return null;
        var value = valueStr.Substring(1, valueStr.Length - 2);
        return op switch
        {
            "eq" => c => getter(c) == value,
            "ne" => c => getter(c) != value,
            _ => null
        };
    }

    private Func<ComplianceRegistry, bool>? BuildIntPredicate(Func<ComplianceRegistry, int?> getter, string op, string valueStr)
    {
        if (!int.TryParse(valueStr, out var value)) return null;
        return op switch
        {
            "eq" => c => getter(c) == value,
            "ne" => c => getter(c) != value,
            "gt" => c => getter(c) > value,
            "ge" => c => getter(c) >= value,
            "lt" => c => getter(c) < value,
            "le" => c => getter(c) <= value,
            _ => null
        };
    }

    private Func<ComplianceRegistry, bool>? BuildBoolPredicate(Func<ComplianceRegistry, bool?> getter, string op, string valueStr)
    {
        if (!bool.TryParse(valueStr, out var value)) return null;
        return op switch
        {
            "eq" => c => getter(c) == value,
            "ne" => c => getter(c) != value,
            _ => null
        };
    }

    // Convert integer ID to deterministic GUID
    public static Guid IntToGuid(int id)
    {
        var bytes = new byte[16];
        BitConverter.GetBytes(id).CopyTo(bytes, 0);
        return new Guid(bytes);
    }

    // Convert GUID back to integer ID (reverse of IntToGuid)
    public static int GuidToInt(Guid guid)
    {
        var bytes = guid.ToByteArray();
        for (int i = 4; i < 16; i++)
        {
            if (bytes[i] != 0)
                throw new ArgumentException($"Invalid ID format: GUID does not match expected pattern", nameof(guid));
        }
        return BitConverter.ToInt32(bytes, 0);
    }
}
