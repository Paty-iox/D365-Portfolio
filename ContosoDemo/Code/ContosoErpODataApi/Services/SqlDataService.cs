using Microsoft.Data.SqlClient;
using ContosoErpODataApi.Models;

namespace ContosoErpODataApi.Services;

public class ParsedFilter
{
    public string WhereClause { get; set; } = "";
    public List<SqlParameter> Parameters { get; set; } = new();
}

public class SqlDataService : ISqlDataService
{
    private readonly string _connectionString;

    private const string VendorColumns = @"
        vendor_id, vendor_number, company_name, payment_terms, credit_limit,
        ytd_purchases, open_po_count, open_po_value, last_payment_date,
        last_payment_amount, average_days_to_pay, vendor_since, vendor_status,
        currency_code, tax_id, duns_number, primary_contact, contact_email, contact_phone";

    public SqlDataService(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<List<VendorMaster>> GetVendorsAsync(ParsedFilter? filter = null, int? top = null, int? skip = null)
    {
        var vendors = new List<VendorMaster>();
        var sql = $"SELECT {VendorColumns} FROM ERP_VendorMaster";

        if (filter != null && !string.IsNullOrEmpty(filter.WhereClause))
        {
            sql += $" WHERE {filter.WhereClause}";
        }

        sql += " ORDER BY vendor_id";

        if (skip.HasValue)
        {
            sql += $" OFFSET @skip ROWS";
            if (top.HasValue)
            {
                sql += $" FETCH NEXT @top ROWS ONLY";
            }
        }
        else if (top.HasValue)
        {
            sql = sql.Replace("SELECT", "SELECT TOP (@top)");
        }

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        using var command = new SqlCommand(sql, connection);

        if (filter?.Parameters != null)
        {
            foreach (var param in filter.Parameters)
            {
                command.Parameters.Add(new SqlParameter(param.ParameterName, param.Value));
            }
        }

        // Add pagination parameters
        if (top.HasValue)
        {
            command.Parameters.AddWithValue("@top", top.Value);
        }
        if (skip.HasValue)
        {
            command.Parameters.AddWithValue("@skip", skip.Value);
        }

        using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            vendors.Add(MapVendor(reader));
        }

        return vendors;
    }

    public async Task<VendorMaster?> GetVendorByIdAsync(int id)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var sql = $"SELECT {VendorColumns} FROM ERP_VendorMaster WHERE vendor_id = @id";
        using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@id", id);

        using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return MapVendor(reader);
        }

        return null;
    }

    public async Task<int> GetCountAsync(ParsedFilter? filter = null)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var sql = "SELECT COUNT(*) FROM ERP_VendorMaster";
        if (filter != null && !string.IsNullOrEmpty(filter.WhereClause))
        {
            sql += $" WHERE {filter.WhereClause}";
        }

        using var command = new SqlCommand(sql, connection);

        if (filter?.Parameters != null)
        {
            foreach (var param in filter.Parameters)
            {
                command.Parameters.Add(new SqlParameter(param.ParameterName, param.Value));
            }
        }

        var result = await command.ExecuteScalarAsync();
        return result != null ? (int)result : 0;
    }

    private static VendorMaster MapVendor(SqlDataReader reader)
    {
        return new VendorMaster
        {
            vendor_id = reader.GetInt32(reader.GetOrdinal("vendor_id")),
            vendor_number = reader.GetString(reader.GetOrdinal("vendor_number")),
            company_name = reader.GetString(reader.GetOrdinal("company_name")),
            payment_terms = reader.GetString(reader.GetOrdinal("payment_terms")),
            credit_limit = reader.IsDBNull(reader.GetOrdinal("credit_limit")) ? null : reader.GetDecimal(reader.GetOrdinal("credit_limit")),
            ytd_purchases = reader.IsDBNull(reader.GetOrdinal("ytd_purchases")) ? null : reader.GetDecimal(reader.GetOrdinal("ytd_purchases")),
            open_po_count = reader.IsDBNull(reader.GetOrdinal("open_po_count")) ? null : reader.GetInt32(reader.GetOrdinal("open_po_count")),
            open_po_value = reader.IsDBNull(reader.GetOrdinal("open_po_value")) ? null : reader.GetDecimal(reader.GetOrdinal("open_po_value")),
            last_payment_date = reader.IsDBNull(reader.GetOrdinal("last_payment_date")) ? null : new DateTimeOffset(reader.GetDateTime(reader.GetOrdinal("last_payment_date")), TimeSpan.Zero),
            last_payment_amount = reader.IsDBNull(reader.GetOrdinal("last_payment_amount")) ? null : reader.GetDecimal(reader.GetOrdinal("last_payment_amount")),
            average_days_to_pay = reader.IsDBNull(reader.GetOrdinal("average_days_to_pay")) ? null : reader.GetInt32(reader.GetOrdinal("average_days_to_pay")),
            vendor_since = reader.IsDBNull(reader.GetOrdinal("vendor_since")) ? null : new DateTimeOffset(reader.GetDateTime(reader.GetOrdinal("vendor_since")), TimeSpan.Zero),
            vendor_status = reader.IsDBNull(reader.GetOrdinal("vendor_status")) ? null : reader.GetString(reader.GetOrdinal("vendor_status")),
            currency_code = reader.GetString(reader.GetOrdinal("currency_code")),
            tax_id = reader.IsDBNull(reader.GetOrdinal("tax_id")) ? null : reader.GetString(reader.GetOrdinal("tax_id")),
            duns_number = reader.IsDBNull(reader.GetOrdinal("duns_number")) ? null : reader.GetString(reader.GetOrdinal("duns_number")),
            primary_contact = reader.IsDBNull(reader.GetOrdinal("primary_contact")) ? null : reader.GetString(reader.GetOrdinal("primary_contact")),
            contact_email = reader.IsDBNull(reader.GetOrdinal("contact_email")) ? null : reader.GetString(reader.GetOrdinal("contact_email")),
            contact_phone = reader.IsDBNull(reader.GetOrdinal("contact_phone")) ? null : reader.GetString(reader.GetOrdinal("contact_phone"))
        };
    }
}
