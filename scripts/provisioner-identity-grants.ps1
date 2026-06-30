<#
.SYNOPSIS
  Ops-gate for the standalone Yorrixx provisioner (ADR-XR-0001 / G3). Creates the provisioner's
  DEDICATED user-assigned managed identity and grants it the highest-privilege roles in the system.
  REVIEW before running. HIGH PRIVILEGE — human/OPS action, intentionally NOT in Terraform/CI.

.DESCRIPTION
  Relocated from yorrixx-app into ai-sdlc-platform with the provisioner (the implementation now lives
  here). The provisioner is the ONLY component that holds cloud-write creds — the whole point of the
  decision/execution split: no LLM/agent app near these grants. The platform's shared agent identity
  (id-aisdlc-dev) must NEVER hold subscription write, so the provisioner gets its own identity, granted:
    - create/delete per-app Azure resources (Web Apps, Functions, Cosmos, storage, MI, App Insights)
        -> Contributor                       (provision + /deprovision teardown)
    - assign per-app RBAC (Website Contributor, Reader, Cosmos/KV data roles)
        -> User Access Administrator         (provision)
    - create/own per-app Entra App registrations + SPs + federated credentials
        -> Graph Application.ReadWrite.OwnedBy   (provision + /deprovision deploy-identity removal)
    - read Cost Management for the /spend query
        -> Cost Management Reader             (/spend; subsumed by subscription Contributor, granted
                                               explicitly so it survives any later tightening of scope)

  Run as a subscription Owner + a directory role that can grant Graph app roles (Privileged Role Admin /
  Global Admin). Idempotent-ish (role-create is a no-op if the assignment exists).

  Terraform (infra/terraform/environments/dev/provisioner.tf) creates the identity, ACR, Container Apps
  environment, and Container App — but NOT these subscription-scoped / Graph grants. They are gated here
  so the dangerous grants require an explicit human run, separate from infra apply.

.NOTES
  Does NOT create the Container App or set the Clerk key — see scripts/provisioner-deploy-runbook.md.
#>

param(
  [Parameter(Mandatory)] [string] $SubscriptionId,
  [string] $ProvisionerRg = "rg-aisdlc-dev",                # platform RG holding the provisioner's UAMI + Container App
  [string] $UamiName       = "id-aisdlc-provisioner-dev",   # must match azurerm_user_assigned_identity.provisioner
  [string] $Location       = "northeurope"
)

$ErrorActionPreference = "Stop"
az account set --subscription $SubscriptionId

Write-Host "1/4  Resolving UAMI $UamiName in $ProvisionerRg (Terraform creates it; this is idempotent) ..."
az identity create -g $ProvisionerRg -n $UamiName -l $Location | Out-Null
$principalId = az identity show -g $ProvisionerRg -n $UamiName --query principalId -o tsv
Write-Host "      principalId = $principalId"

# 2/4  ARM control-plane: Contributor (create/delete resources) + User Access Administrator (assign
#      per-app RBAC) + Cost Management Reader (the /spend query). Subscription-scoped per the agreed
#      'its own subscription-scoped identity' boundary. Tighten to specific RGs (per-app RG + Cosmos RG
#      + KV RG) for least-privilege if desired — then keep Cost Management Reader at subscription scope.
Write-Host "2/4  Assigning Contributor + User Access Administrator + Cost Management Reader (subscription scope) ..."
$subScope = "/subscriptions/$SubscriptionId"
foreach ($role in @("Contributor", "User Access Administrator", "Cost Management Reader")) {
  az role assignment create `
    --assignee-object-id $principalId --assignee-principal-type ServicePrincipal `
    --role $role --scope $subScope | Out-Null
}

# 3/4  Microsoft Graph: Application.ReadWrite.OwnedBy — lets the provisioner create/own per-app App
#      registrations + SPs + federated credentials (deploy identity).
Write-Host "3/4  Granting Graph Application.ReadWrite.OwnedBy ..."
$graphAppId = "00000003-0000-0000-c000-000000000000"      # Microsoft Graph
$graphSpId  = az ad sp show --id $graphAppId --query id -o tsv
$appRoleId  = "18a4783c-866b-4cc7-a460-3d0e455a2f73"       # Application.ReadWrite.OwnedBy
$body = @{ principalId = $principalId; resourceId = $graphSpId; appRoleId = $appRoleId } | ConvertTo-Json -Compress
az rest --method POST `
  --uri "https://graph.microsoft.com/v1.0/servicePrincipals/$principalId/appRoleAssignments" `
  --headers "Content-Type=application/json" --body $body | Out-Null

Write-Host "4/4  Done. UAMI $UamiName is provisioner-ready."
Write-Host ""
Write-Host "REMAINING (deploy-time, see provisioner-deploy-runbook.md):"
Write-Host "  a) Terraform apply creates the Container App with this UAMI assigned (DefaultAzureCredential picks it up)."
Write-Host "  b) Set its config/secrets from Key Vault (kv-aisdlc-81c0):"
Write-Host "       Hosting__SubscriptionId / TenantId / ResourceGroup / Cosmos* / KeyVault* / AppInsightsWorkspaceId"
Write-Host "       Hosting__ClerkSecretKey      (Clerk management key — same custody class as Azure)"
Write-Host "       Hosting__ApiManagedIdentityClientId = clientId of THIS UAMI"
Write-Host "       Provisioner__InboundKey      (X-Platform-Provision-Key the platform presents)"
Write-Host "       Platform__ProvisionResultUrl / Platform__CallbackKey  (Call-2 callback to func-aisdlc-dev-81c0)"
