# Contoso Vendor Risk

Vendor risk assessment and compliance management with multi-factor scoring, bulk validation, and ERP integration via virtual tables.

Risk scoring uses configurable weights/thresholds from contoso_apiconfig. Virtual tables connect to Azure SQL via OData v4.

## Dataverse Plugins (C# .NET 4.6.2)

| Plugin | Message |
|--------|---------|
| CalculateVendorRiskScorePlugin | contoso_CalculateVendorRiskScore |
| BulkComplianceCheckPlugin | contoso_BulkComplianceCheck |

## Azure Functions (.NET 8 Isolated)

| Function | Trigger |
|----------|---------|
| GetVendorMaster | HTTP (OData) |
| GetVendorMasterById | HTTP (OData) |
| GetComplianceRegistry | HTTP (OData) |
| GetComplianceRegistryById | HTTP (OData) |
| GetMetadata | HTTP (OData $metadata) |

## Dataverse Tables

| Table | Type |
|-------|------|
| contoso_vendor | Native |
| contoso_compliancerecord | Native |
| contoso_riskassessment | Native |
| contoso_apiconfig | Native |
| contoso_erpvendormaster | Virtual Table |
| contoso_complianceregistry | Virtual Table |

## Configuration

| Setting | Purpose |
|---------|---------|
| SqlConnectionString | Azure SQL connection for OData API |
| contoso_apiconfig (type=100000000) | Risk scoring weights/thresholds |
| contoso_apiconfig (type=100000001) | Bulk compliance settings |

## Deployment

```bash
# Azure Functions
cd Code/ContosoErpODataApi && func azure functionapp publish <function-app-name>

# Dataverse Plugins
pac plugin push --solution ContosoVendorDemo
```

Solution file: `Solutions/ContosoVendorDemo_1_0_0_1.zip`

## Documentation

- [FDD (Word)](./DEMO_Contoso_Vendor_%20Risk_FDD_v1.0.docx)
