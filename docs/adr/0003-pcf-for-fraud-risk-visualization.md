# ADR-0003: PCF Control for Fraud Risk Visualization

## Status
Accepted

## Date
2026-01-03

## Context
The Apex Claims solution includes a fraud risk score (0-100) with associated risk factors. I needed to provide a visual representation of this data on the Claim form that goes beyond standard Dynamics 365 field rendering.

### Requirements
- Display risk score as a visual gauge or meter
- Show color-coded risk level (green/yellow/red)
- List contributing risk factors
- Update in real-time when score changes

### Options Considered

1. **HTML Web Resource** - Custom JavaScript/HTML embedded in form
2. **Canvas App** - Embedded Power Apps canvas application
3. **PCF Control** - Power Apps Component Framework field control

## Decision
I chose **PCF (Power Apps Component Framework)** to build a custom React-based fraud risk visualization control.

## Rationale

### Why not HTML Web Resource?
- No direct binding to field values; requires manual Xrm.Page calls
- Styling conflicts with Unified Interface
- Not portable across forms or entities
- Limited lifecycle hooks; harder to respond to field changes
- Being deprecated in favor of PCF

### Why not Canvas App?
- Heavyweight for a single-field visualization
- Separate licensing considerations for embedded apps
- Additional latency loading the canvas runtime
- Overkill for displaying data already on the form

### Why PCF?
- First-class field binding with automatic value updates
- Full React/TypeScript support for modern development
- Consistent styling with Unified Interface theme
- Reusable across multiple forms and entities
- Supported in both model-driven apps and portals
- Proper component lifecycle management

## Consequences

### Positive
- Professional, modern UI that matches Dynamics 365 styling
- Strong typing with TypeScript reduces runtime errors
- Component is testable in isolation
- Can be packaged in solutions for ALM
- React ecosystem provides rich visualization libraries

### Negative
- Steeper learning curve than simple web resources
- Requires Node.js toolchain for development
- Bundle size considerations for complex controls
- Must be deployed as part of a solution

## Implementation Details
- Built with React 18 and TypeScript
- Uses bound property for `new_fraudriskscore` field
- Conditional formatting: 0-30 green, 31-60 yellow, 61-100 red
- Risk factors passed as JSON string in secondary field

## Related
- FraudRiskGauge PCF control in `/ApexClaims/Code/PCF/`
