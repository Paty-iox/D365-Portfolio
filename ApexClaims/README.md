# Apex Claims

Claims processing with fraud scoring, geocoding, and weather lookup.

## Plugins (C# .NET 4.6.2)

| Plugin | Trigger |
|--------|---------|
| ClaimGeocoder | Create/Update on Claim |
| ClaimWeather | Create/Update on Claim |

Async to avoid ~1-2s API latency blocking saves.

**Registration:** `new_claim`, Post-Operation, Asynchronous

**Known issue:** Weather API returns UTC dates; display may show previous day for late-evening incidents.

## Azure Functions (Node.js)

| Function | Endpoint |
|----------|----------|
| FraudDetection | POST /api/frauddetection |
| GeocodeLocation | POST /api/geocodelocation |
| WeatherLookup | POST /api/weatherlookup |

Fraud score computed from amount, day-of-week, and description length.

## PCF Control (TypeScript/React)

**FraudRiskBar** - Color-coded risk bar (0-100).

| Property | Type |
|----------|------|
| riskScore | Whole Number |
| showLabel | Boolean |
| showTicks | Boolean |
| enableAnimation | Boolean |
| enablePulse | Boolean |

## Web Resources

| Resource | Type |
|----------|------|
| new_ClaimFormScripts.js | JavaScript |
| new_ClaimLocationMap.html | HTML (Azure Maps) |
| new_policy_form.js | JavaScript |

## Configuration

Environment Variables in Dataverse:

| Variable | Purpose |
|----------|---------|
| `new_geocodeapiurl` | Geocoding function URL |
| `new_geocodeapikey` | Geocoding function key |
| `new_weatherapiurl` | Weather function URL |
| `new_weatherapikey` | Weather function key |
| `new_azuremapskey` | Azure Maps key |

Plugins skip processing if environment variables are not configured.

## Deployment

```bash
# Functions
cd Code/AzureFunctions && func azure functionapp publish <name>

# Plugins
cd Code/Plugins/ClaimGeocoder && dotnet build -c Release

# PCF
cd Code/PCF/FraudRiskBar && npm install && npm run build && pac pcf push

# Portal
pac paportal upload --path Portal
```

## Documentation

- [Video](https://youtu.be/v14AGGMQdQw)
- [Architecture](./Documentation/Apex%20Claims%20Solution%20Architecture.png)
- [FDD](./Documentation/DEMO_Apex_Claims_FDD_v1.0.pdf)
