# AI SDLC Platform

Reusable AI-driven SDLC orchestration platform for GitHub-hosted React/C# web applications deployed to Azure.

## Purpose

This repository contains the AI SDLC platform, not an application website. It orchestrates GitHub Issues, AI personas, pull requests, risk assessment, audit logging, GitHub Actions and Azure deployment workflows for onboarded application repositories.

## Initial architecture

- Azure Durable Functions orchestrator
- Activity functions for each AI SDLC persona
- Shared agent runtime
- GitHub integration service
- Risk rules engine
- Audit logging service
- Model provider abstraction
- Terraform infrastructure

## Application stack targeted by v1

Locked in [ADR-0002 — Generated-App Template Stack](docs/adr/0002-app-template-stack.md). Summary:

- Frontend: React 19 + Vite + TypeScript + TanStack Query + Tailwind + shadcn/ui
- Backend: ASP.NET Core minimal API on .NET 9
- Database: Cosmos DB serverless (one container per user-app)
- Auth: Clerk (Organizations — one Org per user-app)
- Email: SendGrid (templates in user-app code)
- Pipelines: GitHub Actions (OIDC + zip deploy via `azure/webapps-deploy@v3`)
- Hosting: two Azure Web Apps (F1 Free tier) per user-app — frontend + API on one F1 App Service Plan
- Region: UK South (single region, v1)

Per-user-app infrastructure is centrally provisioned by Yorrixx — user-app repos contain code + tests + workflows only.
