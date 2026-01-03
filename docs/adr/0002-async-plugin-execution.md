# ADR-0002: Asynchronous Plugin Execution for External API Calls

## Status
Accepted

## Date
2026-01-03

## Context
The ClaimGeocoder and ClaimWeather plugins call Azure Functions to retrieve geocoding and weather data. I needed to decide whether these plugins should run synchronously (blocking the user save) or asynchronously (processing in the background).

### Options Considered

1. **Synchronous (Pre/Post-Operation)** - Execute during the save transaction
2. **Asynchronous (Post-Operation)** - Execute in background after save completes

## Decision
I chose **Asynchronous Post-Operation** execution for plugins that call external APIs.

## Rationale

### User Experience
- Synchronous execution blocks the form save until the API call completes
- External APIs can take 500ms-3s to respond, causing noticeable UI lag
- Users may perceive the system as slow or unresponsive
- Async execution allows immediate save confirmation

### Reliability
- External API failures in sync mode would block the save operation
- Users would lose their data entry if the API is temporarily unavailable
- Async mode allows the core record to save; geocoding can retry later
- System queue provides automatic retry for transient failures

### Transaction Isolation
- Sync plugins participate in the database transaction
- External API calls should not roll back legitimate data saves
- Async execution decouples external dependencies from core operations

## Consequences

### Positive
- Save operations complete in <500ms regardless of API latency
- Users can continue working immediately after save
- Failed geocoding doesn't prevent claim creation
- Built-in async retry mechanism handles transient failures

### Negative
- Coordinates not immediately available after save (eventual consistency)
- Users may need to refresh form to see geocoded results
- Requires monitoring async job queue for failures
- Slightly more complex debugging (check System Jobs)

## Implementation Notes
- Register plugins as Post-Operation, Asynchronous
- Filter on specific fields (e.g., `new_incidentlocation`) to avoid unnecessary executions
- Log correlation IDs for tracing across plugin → function → external API

## Related
- ADR-0001: Azure Functions for external integrations
