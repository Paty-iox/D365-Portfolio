# ADR-0002: Async Plugin Execution

**Status:** Accepted | **Date:** 2026-01-03

## Context

ClaimGeocoder and ClaimWeather plugins call external APIs. Sync execution would block saves for 500ms-3s.

## Decision

Asynchronous Post-Operation execution.

## Rationale

- Sync blocks saves; users lose data if API unavailable
- Async saves immediately, geocoding happens in background
- Built-in retry handles transient failures

## Consequence

Async System Jobs can take ~30-45s under load. Users may need to refresh to see results.
