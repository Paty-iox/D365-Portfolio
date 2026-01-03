# Architecture Decision Records

This folder contains Architecture Decision Records (ADRs) documenting significant technical decisions made in this project.

## What is an ADR?

An ADR captures a single architectural decision along with its context, rationale, and consequences. They serve as a historical record of why certain approaches were chosen.

## Decisions

| ID | Title | Status |
|----|-------|--------|
| [ADR-0001](./0001-azure-functions-for-external-integrations.md) | Use Azure Functions for External API Integrations | Accepted |
| [ADR-0002](./0002-async-plugin-execution.md) | Asynchronous Plugin Execution for External API Calls | Accepted |
| [ADR-0003](./0003-pcf-for-fraud-risk-visualization.md) | PCF Control for Fraud Risk Visualization | Accepted |

## Template

When adding a new ADR, use this structure:

```markdown
# ADR-NNNN: [Title]

## Status
[Proposed | Accepted | Deprecated | Superseded]

## Date
[YYYY-MM-DD]

## Context
[What is the issue we're addressing?]

## Decision
[What did we decide?]

## Rationale
[Why did we choose this option?]

## Consequences
[What are the positive and negative outcomes?]
```
