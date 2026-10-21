# Azure remote state backend (deployment deferred).
#
# This backend will be enabled after P0-P11 are complete
# and Azure infrastructure deployment is authorized.
#
# terraform {
#   backend "azurerm" {}
# }
#
# Until then, Terraform uses local state for validation and planning.