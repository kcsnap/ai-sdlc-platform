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

- Frontend: React
- Backend: C# / ASP.NET Core Web API
- Database: recommended per solution
- Infrastructure: Terraform
- Pipelines: GitHub Actions
- Hosting: Azure Static Web Apps, Azure App Service, Azure Functions and Azure database services
