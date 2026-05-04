# ADR 0001: Initial AI SDLC Platform Architecture

## Status

Accepted

## Context

The platform must orchestrate a reusable AI-driven SDLC for GitHub-hosted React/C# web applications deployed to Azure.

## Decision

Use Azure Durable Functions as the workflow orchestrator, with activity functions for AI SDLC personas, GitHub integration, risk assessment, audit logging and model provider abstraction.

## Consequences

The workflow is asynchronous, stateful, auditable and able to pause for approvals, GitHub Actions results and human review.
