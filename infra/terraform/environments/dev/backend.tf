terraform {
  backend "azurerm" {
    resource_group_name  = "rg-aisdlc-tfstate"
    storage_account_name = "aisdlctfstate81c0"
    container_name       = "tfstate"
    key                  = "dev.terraform.tfstate"
  }
}
