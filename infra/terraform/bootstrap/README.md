# Bootstrap

Run this once before initialising Terraform to create the remote state storage account.

## Prerequisites

- Azure CLI installed and logged in (`az login`)
- Contributor access on the subscription

## Run

```powershell
.\bootstrap.ps1
```

The script creates:
- Resource group `rg-aisdlc-tfstate`
- Storage account `aisdlctfstate81c0` (globally unique — change the suffix if the name is taken)
- Blob container `tfstate`

## After bootstrap

```bash
cd infra/terraform/environments/dev
terraform init
terraform plan
```
