using ContosoErpODataApi.Models;

namespace ContosoErpODataApi.Services;

public interface ISqlDataService
{
    Task<List<VendorMaster>> GetVendorsAsync(ParsedFilter? filter = null, int? top = null, int? skip = null);
    Task<VendorMaster?> GetVendorByIdAsync(int id);
    Task<int> GetCountAsync(ParsedFilter? filter = null);
}
