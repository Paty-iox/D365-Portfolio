using System.ComponentModel.DataAnnotations;

namespace ContosoErpODataApi.Models;

public class ComplianceRegistry
{
    [Key]
    public int registry_id { get; set; }

    [Required]
    [MaxLength(20)]
    public string vendor_number { get; set; } = string.Empty;

    [MaxLength(20)]
    public string? sam_status { get; set; }

    public DateTimeOffset? sam_expiry { get; set; }

    public int? osha_violation_count { get; set; }

    public DateTimeOffset? osha_last_inspection { get; set; }

    public bool? debarred { get; set; }
}
