# Provisioner deploy runbook — ai-sdlc-platform (ADR-XR-0001 / G3)

Stands up the standalone `Yorrixx.Provisioner` as an Azure Container App **in the platform's resource
group** (`rg-aisdlc-dev`), with a **dedicated** managed identity (the platform's shared agent identity
`id-aisdlc-dev` must never hold subscription write). Infra is Terraform; the high-privilege grants are a
separate human-gated script; image build + secret config are deploy-time.

> ⚠️ **STAGE 2 — requires explicit human authorization.** Applying the Terraform creates cloud resources
> (Container App, ACR, environment, a UAMI) and running the grants script assigns subscription-scoped +
> Graph privileges. Do not run any of this without sign-off. Nothing here runs on merge.

> Output you owe back: the **provisioner base URL** (Step 4) → the platform sets its `ProvisionerUrl` at G4.

---

## Step 0 — names
```powershell
$Sub   = "<subscription-id>"            # 66673944-…-81c0
$Rg    = "rg-aisdlc-dev"
$Kv    = "kv-aisdlc-81c0"
$App   = "ca-aisdlc-provisioner-dev"
$Acr   = "acraisdlcdev81c0"
$Image = "yorrixx-provisioner"
$Uami  = "id-aisdlc-provisioner-dev"    # dedicated — created by Terraform, granted by the script
az account set --subscription $Sub
```

## Step 1 — apply infra (Terraform)
Creates the UAMI, ACR (`AcrPull` for the UAMI), Container Apps environment, and the Container App.
```powershell
cd infra/terraform/environments/dev
terraform plan -out tfplan      # review — confirm only the provisioner.tf resources are added
terraform apply tfplan
```

## Step 2 — grant the high-privilege roles (human/OPS)
Contributor + User Access Administrator + **Cost Management Reader** (subscription scope) + Graph
`Application.ReadWrite.OwnedBy`. **Review the script first.**
```powershell
./scripts/provisioner-identity-grants.ps1 -SubscriptionId $Sub
```

## Step 3 — build + push the image
```powershell
az acr login --name $Acr
$Tag = (Get-Date -Format "yyyyMMddHHmmss")
$Ref = "$Acr.azurecr.io/$Image`:$Tag"
docker build -f src/Yorrixx.Provisioner/Dockerfile -t $Ref .   # net8 image; build context = repo root
docker push $Ref
az containerapp update -n $App -g $Rg --image $Ref | Out-Null
```

## Step 4 — config (Hosting:* + platform/provisioner keys, from Key Vault)
```powershell
$UamiClientId = az identity show -g $Rg -n $Uami --query clientId -o tsv
$inbound = az keyvault secret show --vault-name $Kv --name ProvisionerInboundKey      --query value -o tsv
$cbKey   = az keyvault secret show --vault-name $Kv --name ProvisionResultCallbackKey --query value -o tsv

$pairs = @(
  "AZURE_CLIENT_ID=$UamiClientId"        # DefaultAzureCredential selects the dedicated UAMI
  "Hosting__ApiManagedIdentityClientId=$UamiClientId"
  "Provisioner__InboundKey=$inbound"
  "Platform__ProvisionResultUrl=https://func-aisdlc-dev-81c0.azurewebsites.net/api/provision-result"
  "Platform__CallbackKey=$cbKey"
  # plus Hosting__SubscriptionId / TenantId / ResourceGroup / Cosmos* / KeyVault* /
  #      AppInsightsWorkspaceId / ClerkSecretKey  — from KV / the dev config set.
)
az containerapp update -n $App -g $Rg --set-env-vars @pairs | Out-Null
```

## Step 5 — base URL → send to the platform
```powershell
$Fqdn = az containerapp show -n $App -g $Rg --query "properties.configuration.ingress.fqdn" -o tsv
"PROVISIONER BASE URL = https://$Fqdn"   # platform sets ProvisionerUrl at G4 (do NOT flip BuildApiUrl early)
```

## Step 6 — smoke test
```powershell
(Invoke-WebRequest "https://$Fqdn/health").Content   # -> {"status":"ok"}
```

---

## Ongoing deploys (after STAGE 2)
The gated CI job `deploy-provisioner` (`.github/workflows/ci.yml`) automates Steps 3 once the repo
variable `PROVISIONER_DEPLOY_ENABLED=true` and `PROVISIONER_ACR_NAME` / `PROVISIONER_APP_NAME` /
`PROVISIONER_RG` are set. It is **inert until then** — merging this PR does not deploy anything.
