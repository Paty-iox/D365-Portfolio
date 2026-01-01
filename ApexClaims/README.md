# Apex Claims

**Insurance Claims Management System for Dynamics 365**

## Business Problem

Insurance companies need to process claims efficiently while detecting fraudulent submissions. Manual fraud assessment is time-consuming and inconsistent. Claims adjusters also need accurate incident location data and weather conditions at the time of the incident to validate claims.

## Solution

A Dynamics 365-based claims management system with:

- **Automated Fraud Scoring** - Real-time risk assessment based on claim characteristics
- **Location Intelligence** - Automatic geocoding of incident locations via Azure Maps
- **Weather Integration** - Historical weather data capture for claim validation
- **Customer Portal** - Self-service claim submission and tracking

## Components

### Plugins (C# .NET 4.6.2)

| Plugin | Trigger | Description |
|--------|---------|-------------|
| ClaimGeocoder | Create/Update on Claim | Calls Azure Function to geocode incident location, updates lat/long fields |
| ClaimWeather | Create/Update on Claim | Retrieves historical weather data for incident date and location |

**Registration:**
- Entity: `new_claim`
- Stage: Post-Operation
- Mode: Synchronous

### Azure Functions (Node.js)

| Function | Endpoint | Description |
|----------|----------|-------------|
| FraudDetection | POST /api/frauddetection | Calculates fraud risk score (0-100) based on claim attributes |
| GeocodeLocation | POST /api/geocodelocation | Converts address to coordinates using Azure Maps |
| WeatherLookup | POST /api/weatherlookup | Retrieves weather conditions for date/location |

**Fraud Risk Factors:**
- Claim amount thresholds
- Weekend incidents
- Rapid or delayed reporting
- Vague location/description
- Total loss claims
- Claim type modifiers

### PCF Control (TypeScript/React)

**FraudRiskBar** - Visual risk indicator component

- Displays fraud score as color-coded progress bar
- Risk levels: Low (green), Medium (yellow), High (orange), Critical (red)
- Configurable animation and pulse effects
- Responsive design with tick marks

Properties:
| Property | Type | Description |
|----------|------|-------------|
| riskScore | Whole Number | Score value (0-100) |
| showLabel | Boolean | Display score label |
| showTicks | Boolean | Show threshold markers |
| enableAnimation | Boolean | Animate on load |
| enablePulse | Boolean | Pulse effect for high scores |

### Web Resources

| Resource | Type | Description |
|----------|------|-------------|
| new_ClaimFormScripts.js | JavaScript | Form event handlers, field validation |
| new_ClaimLocationMap.html | HTML | Azure Maps integration for incident visualization |
| new_policy_form.js | JavaScript | Policy form scripts |

### Power Pages Portal

Customer-facing portal for:
- New claim submission
- Claim status tracking
- Document upload
- Communication history

## Folder Structure

```
ApexClaims/
├── Code/
│   ├── AzureFunctions/
│   │   ├── FraudDetection/
│   │   ├── GeocodeLocation/
│   │   └── WeatherLookup/
│   ├── Plugins/
│   │   ├── ClaimGeocoder/
│   │   └── ClaimWeather/
│   ├── PCF/
│   │   └── FraudRiskBar/
│   └── WebResources/
├── Portal/
│   ├── web-pages/
│   ├── web-templates/
│   └── content-snippets/
├── Solutions/
└── Documentation/
    ├── Apex Claims Solution Architecture.png
    └── DEMO_Apex_Claims_FDD_v1.0.pdf
```

## Setup

### Prerequisites

- Dynamics 365 environment with Dataverse
- Azure subscription
- Azure Maps account
- Node.js 18+ (for Azure Functions)
- .NET Framework 4.6.2 SDK (for plugins)
- Power Platform CLI (`pac`)

### Deployment Steps

1. **Deploy Azure Functions**
   ```bash
   cd Code/AzureFunctions
   func azure functionapp publish <function-app-name>
   ```

2. **Build and Register Plugins**
   ```bash
   cd Code/Plugins/ClaimGeocoder
   dotnet build -c Release
   # Register via Plugin Registration Tool
   ```

3. **Build and Deploy PCF Control**
   ```bash
   cd Code/PCF/FraudRiskBar
   npm install
   npm run build
   pac pcf push
   ```

4. **Import D365 Solution**
   - Import solution from `Solutions/` folder
   - Configure Environment Variables for API URLs and keys

5. **Deploy Portal**
   ```bash
   pac paportal upload --path Portal
   ```

## Configuration

### Environment Variables

| Variable | Description |
|----------|-------------|
| new_geocodeapiurl | Azure Function URL for geocoding |
| new_geocodeapikey | Azure Function key |
| new_weatherapiurl | Weather API endpoint |
| new_weatherapikey | Weather API key |
| new_fraudapiurl | Fraud detection endpoint |

## Documentation

- [Solution Architecture Diagram](./Documentation/Apex%20Claims%20Solution%20Architecture.png)
- [Functional Design Document](./Documentation/DEMO_Apex_Claims_FDD_v1.0.pdf)
