using System.ComponentModel.DataAnnotations;

namespace ContosoErpODataApi.Models;

public class VendorMaster
{
    [Key]
    public int vendor_id { get; set; }

    [Required]
    [MaxLength(20)]
    public string vendor_number { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string company_name { get; set; } = string.Empty;

    [Required]
    [MaxLength(20)]
    public string payment_terms { get; set; } = string.Empty;

    public decimal? credit_limit { get; set; }

    public decimal? ytd_purchases { get; set; }

    public int? open_po_count { get; set; }

    public decimal? open_po_value { get; set; }

    public DateTimeOffset? last_payment_date { get; set; }

    public decimal? last_payment_amount { get; set; }

    public int? average_days_to_pay { get; set; }

    public DateTimeOffset? vendor_since { get; set; }

    [MaxLength(20)]
    public string? vendor_status { get; set; }

    [Required]
    [MaxLength(3)]
    public string currency_code { get; set; } = "USD";

    [MaxLength(20)]
    public string? tax_id { get; set; }

    [MaxLength(15)]
    public string? duns_number { get; set; }

    [MaxLength(100)]
    public string? primary_contact { get; set; }

    [MaxLength(100)]
    public string? contact_email { get; set; }

    [MaxLength(20)]
    public string? contact_phone { get; set; }
}
