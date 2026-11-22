# ADR-0003: PCF for Fraud Risk Visualization

**Status:** Accepted | **Date:** 2026-01-03

## Context

Need visual fraud score display (0-100) on Claim form. Options: HTML web resource, Canvas App, or PCF.

## Decision

PCF control with React/TypeScript.

## Rationale

- HTML web resource: no direct field binding, so the score would have to be passed in by form script
- Canvas App too heavy for single field
- PCF: field binding, React support, reusable

## Consequence

Bundle ~180 KB; theming tied to default UCI palette.
