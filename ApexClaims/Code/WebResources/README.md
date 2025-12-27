# Claim Location Map Web Resources

This folder contains web resources for displaying an Azure Maps view of the incident location on Claim records.

## Files

| File | Type | Description |
|------|------|-------------|
| new_ClaimLocationMap.html | HTML | Azure Maps iframe showing incident location |
| new_ClaimFormScripts.js | JavaScript | Form event handlers for map integration |

## Deployment Steps

### Step 1: Upload Web Resources

1. Go to [make.powerapps.com](https://make.powerapps.com)
2. Select your environment (DeveloperDEMO)
3. Navigate to **Solutions** → **DEMOSOLUTION**
4. Click **+ New** → **More** → **Web resource**

#### Upload HTML Web Resource
| Field | Value |
|-------|-------|
| Display name | Claim Location Map |
| Name | new_ClaimLocationMap |
| Type | Webpage (HTML) |
| Upload file | new_ClaimLocationMap.html |

Click **Save**.

#### Upload JavaScript Web Resource
| Field | Value |
|-------|-------|
| Display name | Claim Form Scripts |
| Name | new_ClaimFormScripts |
| Type | Script (JScript) |
| Upload file | new_ClaimFormScripts.js |

Click **Save**.

### Step 2: Add Map to Claim Form

1. In DEMOSOLUTION, navigate to **Tables** → **Claim** → **Forms**
2. Open the **Main** form (Information form)
3. Click **+ Component** in the left panel

#### Add a New Section for the Map
1. Add a new **1-column section**
2. Label: "Location Map"
3. Position it where you want the map (e.g., below incident location fields)

#### Add the HTML Web Resource
1. With the section selected, click **+ Component**
2. Select **HTML web resource (iframe)** (under "More components" if not visible)
3. Select **new_ClaimLocationMap**
4. Configure:
   | Setting | Value |
   |---------|-------|
   | Name | WebResource_ClaimLocationMap |
   | Label | (uncheck "Show label") |
   | Row span | 10 |
   | Restrict cross-frame scripting | Unchecked |

5. Click **Done**

### Step 3: Configure Form Events

1. In the form designer, click **Form properties** (or Settings gear icon)
2. Go to the **Events** tab

#### Add Form Library
1. Under **Form libraries**, click **+ Add library**
2. Search for and select **new_ClaimFormScripts**

#### Add OnLoad Event
1. Under **Event Handlers**, select **On Load** event
2. Click **+ Event Handler**
3. Configure:
   | Field | Value |
   |-------|-------|
   | Library | new_ClaimFormScripts |
   | Function | ApexInsurance.ClaimForm.onLoad |
   | Pass execution context | Checked |

#### Add OnChange Events for Coordinates
1. Select the **new_incidentlatitude** field in the form
2. In the properties panel, go to **Events**
3. Add **On Change** event:
   | Field | Value |
   |-------|-------|
   | Library | new_ClaimFormScripts |
   | Function | ApexInsurance.ClaimForm.onCoordinatesChange |
   | Pass execution context | Checked |

4. Repeat for **new_incidentlongitude** field

### Step 4: Save and Publish

1. Click **Save**
2. Click **Publish**

## Testing

1. Open the Model-Driven App
2. Navigate to **Claims**
3. Open an existing claim that has coordinates
4. Verify the map displays with a red pin at the location
5. Create a new claim with an incident location
6. Save the record
7. Verify coordinates are populated (by plugin) and map updates

## Configuration

### Azure Maps Key
The Azure Maps key is embedded in the HTML file. If you need to update it:

1. Open `new_ClaimLocationMap.html`
2. Find the line: `var AZURE_MAPS_KEY = '...'`
3. Replace with your new key
4. Re-upload the web resource

### Web Resource Control Name
The JavaScript expects the web resource control to be named `WebResource_ClaimLocationMap`. If you use a different name:

1. Open `new_ClaimFormScripts.js`
2. Update the constant: `var WEB_RESOURCE_NAME = "YourControlName"`
3. Re-upload the web resource

## Troubleshooting

### Map not showing
- Check browser console (F12) for errors
- Verify Azure Maps key is valid
- Ensure web resource control name matches: `WebResource_ClaimLocationMap`

### Map shows "No Location Set"
- Verify the claim has latitude and longitude values
- Check that the coordinate fields are not hidden or have null values

### Map not updating when coordinates change
- Verify OnChange events are configured for both lat and lon fields
- Ensure "Pass execution context" is checked
- Check browser console for JavaScript errors

### Cross-origin errors
- Ensure "Restrict cross-frame scripting" is unchecked on the web resource
- The HTML file is designed to work within Dynamics 365 forms

## Files Structure

```
src/webresources/
├── new_ClaimLocationMap.html    # Azure Maps iframe
├── new_ClaimFormScripts.js      # Form event handlers
└── README.md                    # This file
```
