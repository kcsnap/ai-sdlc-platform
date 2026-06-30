# Provisioner deploy runbook — ai-sdlc-platform (ADR-XR-0001 / G3)

Stands up the standalone `Yorrixx.Provisioner` as an **Azure Function** (Flex Consumption / FC1,
dotnet-isolated 8.0) **in the platform's resource group** (`rg-aisdlc-dev`), with a **dedicated** managed
identity (the platform's shared agent identity `id-aisdlc-dev` must never hold subscription write). Same
stack as the platform Function App + admin app — no container registry. Infra is Terraform; the
high-privilege grants are a separate human-gated script; secret config is deploy-time.

> ⚠️ **STAGE 2 — requires explicit human authorization.** Applying the Terraform creates cloud resources
> (Function app, FC1 plan, a storage account, a UAMI) and running the grants script assigns
> subscription-scoped + Graph privileges. Nothing here runs on merge.

> Output you owe back: the **provisioner base URL** (Step 5) → the platform sets its `ProvisionerUrl` at G4.

---

## Step 0 — names
```powershell
$Sub   = "<subscription-id>"            # 66673944-…-81c0
$Rg    = "rg-aisdlc-dev"
$Kv    = "kv-aisdlc-81c0"
$App   = "func-aisdlc-prov-dev-81c0"
$Uami  = "id-aisdlc-provisioner-dev"    # dedicated — created by Terraform, granted by the script
az account set --subscription $Sub
```

## Step 1 — apply infra (Terraform, apply -target on the provisioner resources only)
Creates the UAMI, the provisioner storage (+ blob/queue/table RBAC for the UAMI), the FC1 plan, and the
Function app. `-target` keeps the unrelated environments/dev WIP untouched.
```powershell
cd infra/terraform/environments/dev
terraform plan `
  -target=azurerm_user_assigned_identity.provisioner `
  -target=module.provisioner_storage `
  -target=azurerm_role_assignment.provisioner_storage_blob `
  -target=azurerm_role_assignment.provisioner_storage_queue `
  -target=azurerm_role_assignment.provisioner_storage_table `
  -target=module.provisioner_function `
  -out tfplan
terraform apply tfplan
```
> Pre-apply: parameterize the function-app module so it doesn't push the orchestrator's
> Anthropic/GitHub/Audit settings onto the provisioner (see the note in provisioner.tf).

## Step 2 — grant the high-privilege roles (human/OPS)
Contributor + User Access Administrator + **Cost Management Reader** (subscription) + Graph
`Application.ReadWrite.OwnedBy`. **Review the script first.**
```powershell
./scripts/provisioner-identity-grants.ps1 -SubscriptionId $Sub
```

## Step 3 — deploy the code
Either set the gated CI variables (PROVISIONER_DEPLOY_ENABLED=true, PROVISIONER_APP_NAME=$App) and let CI
publish on the next push to main, or publish directly:
```powershell
func azure functionapp publish $App --dotnet-isolated   # from src/Yorrixx.Provisioner
```

## Step 4 — secret config (from Key Vault; non-secrets are already set by Terraform)
```powershell
$inbound = az keyvault secret show --vault-name $Kv --name ProvisionerInboundKey      --query value -o tsv
$cbKey   = az keyvault secret show --vault-name $Kv --name ProvisionResultCallbackKey --query value -o tsv
az functionapp config appsettings set -n $App -g $Rg --settings `
  "Provisioner__InboundKey=$inbound" `
  "Platform__ProvisionResultUrl=https://func-aisdlc-dev-81c0.azurewebsites.net/api/provision-result" `
  "Platform__CallbackKey=$cbKey" `
  # plus Hosting__TenantId / Cosmos* / KeyVault* / AppInsightsWorkspaceId / ClerkSecretKey as needed.
# The provisioner UAMI also needs Key Vault Secrets User on kv-aisdlc-81c0 to read these at runtime.
```

## Step 5 — base URL → send to the platform
```powershell
$Host = az functionapp show -n $App -g $Rg --query "defaultHostName" -o tsv
"PROVISIONER BASE URL = https://$Host"   # platform sets ProvisionerUrl at G4 (do NOT flip BuildApiUrl early)
```

## Step 6 — smoke test
```powershell
(Invoke-WebRequest "https://$Host/health").Content   # -> {"status":"ok"}  (routePrefix is "" — no /api)
```

---

## Notes
- Routes have NO `/api` prefix (host.json `routePrefix: ""`), so /provision, /spend, /health match the
  platform's existing ProvisionerClient base URL — no platform change needed.
- Long work is queue-decoupled: /provision and /deprovision return 202 + enqueue; the queue-triggered
  workers run inside the FC1 per-invocation budget. Status persists in a Table so GET /provision/{buildId}
  survives restarts.
