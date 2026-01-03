# ADR-0001: Azure Functions for External APIs

**Status:** Accepted | **Date:** 2026-01-03

## Context

Apex Claims needs geocoding, weather, and fraud scoring APIs. Options: direct plugin calls, Logic Apps, or Functions.

## Decision

Azure Functions as the integration layer.

## Rationale

- Plugin sandbox timeout (2 min) hit twice during testing
- Logic Apps too heavy for simple request/response
- Functions: fast cold start, easy local dev, retry logic

## Consequence

Key Vault not wired in demo; app settings hold keys.
