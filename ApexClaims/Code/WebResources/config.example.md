# Web Resources Configuration

The web resources require the following Dataverse Environment Variables to be configured:

## Required Environment Variables

| Schema Name | Display Name | Description |
|-------------|--------------|-------------|
| `new_azuremapskey` | Azure Maps Key | Azure Maps subscription key for geocoding and map display |

## Setup Instructions

1. In your Dynamics 365 environment, go to **Settings** > **Solutions**
2. Open the Apex Claims solution
3. Navigate to **Environment Variables**
4. Create or update the following variables with your actual values:

### new_azuremapskey
- **Type**: Text
- **Value**: Your Azure Maps subscription key (from Azure Portal > Azure Maps Account > Authentication)

## Notes

- Never commit actual API keys to source control
- Environment variables allow different values per environment (dev/test/prod)
- Keys are retrieved at runtime via the Dataverse Web API
