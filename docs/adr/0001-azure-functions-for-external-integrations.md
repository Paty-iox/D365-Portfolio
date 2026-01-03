# ADR-0001: Use Azure Functions for External API Integrations

## Status
Accepted

## Date
2026-01-03

## Context
The Apex Claims solution requires integration with external services:
- Azure Maps for geocoding incident locations
- Weather API for capturing conditions at time of incident
- Fraud detection scoring engine

I needed to decide how to call these external APIs from Dynamics 365.

### Options Considered

1. **Direct API calls from plugins** - Call external APIs directly from C# plugin code
2. **Logic Apps** - Use Logic Apps as an integration layer
3. **Azure Functions** - Use Azure Functions as a lightweight API gateway

## Decision
I chose **Azure Functions** as the integration layer between Dynamics 365 and external services.

## Rationale

### Why not direct plugin calls?
- Plugins have a 2-minute execution timeout; external API latency is unpredictable
- Sandbox isolation limits outbound connections and can cause intermittent failures
- No built-in retry logic or circuit breaker patterns
- Harder to test and debug external integrations within plugin context

### Why not Logic Apps?
- Higher latency due to workflow orchestration overhead
- More expensive for high-frequency, simple request/response patterns
- Overkill for straightforward API proxy scenarios
- Better suited for complex orchestration rather than simple transformations

### Why Azure Functions?
- Sub-second cold start times on Consumption plan
- Native HTTP trigger support with minimal boilerplate
- Easy local development and testing with Azure Functions Core Tools
- Cost-effective for sporadic, event-driven workloads
- Can implement retry logic, caching, and error handling
- Secrets managed via Application Settings / Key Vault integration

## Consequences

### Positive
- Plugins remain lightweight and focused on Dataverse operations
- External integrations are independently deployable and testable
- Clear separation of concerns between CRM logic and integration logic
- Easier to swap external providers without modifying plugins

### Negative
- Additional Azure resource to manage and monitor
- Network hop adds ~50-200ms latency per call
- Requires function key management for security

## Related
- ADR-0002: Async plugin execution for external calls
