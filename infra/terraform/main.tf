# Root-level provider constraints — inherited by all modules.
# Each environment (environments/dev, environments/prod) has its own
# backend.tf and terraform{} block with the remote state configuration.
# Run terraform from within the environment directory, not here.

terraform {
  required_version = ">= 1.6.0"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 3.100"
    }
  }
}
