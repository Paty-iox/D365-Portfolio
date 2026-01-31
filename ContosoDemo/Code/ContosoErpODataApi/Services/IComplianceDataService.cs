using ContosoErpODataApi.Models;

namespace ContosoErpODataApi.Services;

public interface IComplianceDataService
{
    Task<List<ComplianceRegistry>> GetComplianceRecordsAsync(
        Func<ComplianceRegistry, bool>? filter = null,
        int? top = null,
        int? skip = null);
    Task<ComplianceRegistry?> GetComplianceByIdAsync(int id);
    Task<int> GetCountAsync(Func<ComplianceRegistry, bool>? filter = null);
}
