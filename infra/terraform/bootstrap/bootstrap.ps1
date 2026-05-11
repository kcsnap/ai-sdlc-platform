# One-time bootstrap: creates the remote Terraform state storage account.
# Run this once before running terraform init in environments/dev.

param(
    [string]$SubscriptionId = "66673944-b353-4256-b71c-0cd8751c81c0",
    [string]$ResourceGroup  = "rg-aisdlc-tfstate",
    [string]$Location       = "uksouth",
    [string]$StorageAccount = "aisdlctfstate81c0",
    [string]$Container      = "tfstate"
)

Write-Host "Setting subscription..."
az account set --subscription $SubscriptionId

Write-Host "Creating resource group $ResourceGroup..."
az group create --name $ResourceGroup --location $Location --output none

Write-Host "Creating storage account $StorageAccount..."
az storage account create `
    --name $StorageAccount `
    --resource-group $ResourceGroup `
    --location $Location `
    --sku Standard_LRS `
    --allow-blob-public-access false `
    --min-tls-version TLS1_2 `
    --output none

Write-Host "Creating state container..."
az storage container create `
    --name $Container `
    --account-name $StorageAccount `
    --output none

Write-Host ""
Write-Host "Bootstrap complete. Storage account: $StorageAccount, container: $Container"
Write-Host "You can now run: terraform init in infra/terraform/environments/dev"
