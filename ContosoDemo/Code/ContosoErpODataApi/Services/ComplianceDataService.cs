using ContosoErpODataApi.Models;

namespace ContosoErpODataApi.Services;

public class ComplianceDataService : IComplianceDataService
{
    private static readonly List<ComplianceRegistry> _mockData = new()
    {
        new() { registry_id = 1, vendor_number = "V000001", sam_status = "Active", sam_expiry = new DateTimeOffset(2025, 12, 31, 0, 0, 0, TimeSpan.Zero), osha_violation_count = 0, osha_last_inspection = new DateTimeOffset(2024, 6, 15, 0, 0, 0, TimeSpan.Zero), debarred = false },
        new() { registry_id = 2, vendor_number = "V000002", sam_status = "Active", sam_expiry = new DateTimeOffset(2025, 8, 15, 0, 0, 0, TimeSpan.Zero), osha_violation_count = 1, osha_last_inspection = new DateTimeOffset(2024, 3, 20, 0, 0, 0, TimeSpan.Zero), debarred = false },
        new() { registry_id = 3, vendor_number = "V000003", sam_status = "Expired", sam_expiry = new DateTimeOffset(2024, 6, 30, 0, 0, 0, TimeSpan.Zero), osha_violation_count = 0, osha_last_inspection = new DateTimeOffset(2023, 11, 10, 0, 0, 0, TimeSpan.Zero), debarred = false },
        new() { registry_id = 4, vendor_number = "V000004", sam_status = "Active", sam_expiry = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero), osha_violation_count = 2, osha_last_inspection = new DateTimeOffset(2024, 9, 5, 0, 0, 0, TimeSpan.Zero), debarred = false },
        new() { registry_id = 5, vendor_number = "V000005", sam_status = "Active", sam_expiry = new DateTimeOffset(2025, 5, 20, 0, 0, 0, TimeSpan.Zero), osha_violation_count = 0, osha_last_inspection = new DateTimeOffset(2024, 1, 12, 0, 0, 0, TimeSpan.Zero), debarred = false },
        new() { registry_id = 6, vendor_number = "V000006", sam_status = "Inactive", sam_expiry = new DateTimeOffset(2023, 12, 31, 0, 0, 0, TimeSpan.Zero), osha_violation_count = 3, osha_last_inspection = new DateTimeOffset(2024, 7, 22, 0, 0, 0, TimeSpan.Zero), debarred = false },
        new() { registry_id = 7, vendor_number = "V000007", sam_status = "Active", sam_expiry = new DateTimeOffset(2025, 11, 30, 0, 0, 0, TimeSpan.Zero), osha_violation_count = 0, osha_last_inspection = new DateTimeOffset(2024, 4, 8, 0, 0, 0, TimeSpan.Zero), debarred = false },
        new() { registry_id = 8, vendor_number = "V000008", sam_status = "Pending", sam_expiry = new DateTimeOffset(2025, 6, 30, 0, 0, 0, TimeSpan.Zero), osha_violation_count = 0, osha_last_inspection = new DateTimeOffset(2024, 3, 15, 0, 0, 0, TimeSpan.Zero), debarred = false },
        new() { registry_id = 9, vendor_number = "V000009", sam_status = "Active", sam_expiry = new DateTimeOffset(2025, 9, 15, 0, 0, 0, TimeSpan.Zero), osha_violation_count = 5, osha_last_inspection = new DateTimeOffset(2024, 10, 1, 0, 0, 0, TimeSpan.Zero), debarred = false },
        new() { registry_id = 10, vendor_number = "V000010", sam_status = "Active", sam_expiry = new DateTimeOffset(2026, 1, 31, 0, 0, 0, TimeSpan.Zero), osha_violation_count = 0, osha_last_inspection = new DateTimeOffset(2024, 2, 28, 0, 0, 0, TimeSpan.Zero), debarred = false },
        new() { registry_id = 11, vendor_number = "V000011", sam_status = "Expired", sam_expiry = new DateTimeOffset(2024, 3, 15, 0, 0, 0, TimeSpan.Zero), osha_violation_count = 1, osha_last_inspection = new DateTimeOffset(2023, 8, 20, 0, 0, 0, TimeSpan.Zero), debarred = false },
        new() { registry_id = 12, vendor_number = "V000012", sam_status = "Active", sam_expiry = new DateTimeOffset(2025, 7, 10, 0, 0, 0, TimeSpan.Zero), osha_violation_count = 0, osha_last_inspection = new DateTimeOffset(2024, 5, 15, 0, 0, 0, TimeSpan.Zero), debarred = false },
        new() { registry_id = 13, vendor_number = "V000013", sam_status = "Active", sam_expiry = new DateTimeOffset(2025, 10, 25, 0, 0, 0, TimeSpan.Zero), osha_violation_count = 0, osha_last_inspection = new DateTimeOffset(2024, 8, 30, 0, 0, 0, TimeSpan.Zero), debarred = true },
        new() { registry_id = 14, vendor_number = "V000014", sam_status = "Active", sam_expiry = new DateTimeOffset(2026, 2, 28, 0, 0, 0, TimeSpan.Zero), osha_violation_count = 0, osha_last_inspection = new DateTimeOffset(2024, 11, 5, 0, 0, 0, TimeSpan.Zero), debarred = false }
    };

    public Task<List<ComplianceRegistry>> GetComplianceRecordsAsync(
        Func<ComplianceRegistry, bool>? filter = null,
        int? top = null,
        int? skip = null)
    {
        IEnumerable<ComplianceRegistry> query = _mockData;

        if (filter != null)
            query = query.Where(filter);

        if (skip.HasValue)
            query = query.Skip(skip.Value);

        if (top.HasValue)
            query = query.Take(top.Value);

        return Task.FromResult(query.ToList());
    }

    public Task<ComplianceRegistry?> GetComplianceByIdAsync(int id)
    {
        return Task.FromResult(_mockData.FirstOrDefault(c => c.registry_id == id));
    }

    public Task<int> GetCountAsync(Func<ComplianceRegistry, bool>? filter = null)
    {
        if (filter != null)
            return Task.FromResult(_mockData.Count(filter));
        return Task.FromResult(_mockData.Count);
    }
}
