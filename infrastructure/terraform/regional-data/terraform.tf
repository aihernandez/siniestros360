terraform {
  required_version = ">= 1.17.1, < 2.0.0"
  backend "azurerm" {}
  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "= 4.81.0"
    }
    postgresql = {
      source  = "cyrilgdn/postgresql"
      version = "= 1.27.0"
    }
    random = {
      source  = "hashicorp/random"
      version = "= 3.9.1"
    }
  }
}
