module "azurerm_key_vault_tfvars" {
  source = "github.com/DFE-Digital/terraform-azurerm-key-vault-tfvars?ref=v0.5.5"

  environment                             = local.environment
  project_name                            = local.project_name
  existing_resource_group                 = local.existing_resource_group
  azure_location                          = local.azure_location
  key_vault_access_use_rbac_authorization = true
  key_vault_access_users                  = []
  key_vault_access_ipv4                   = local.key_vault_access_ipv4
  tfvars_filename                         = local.tfvars_filename
  diagnostic_log_analytics_workspace_id   = module.azure_container_apps_hosting.azurerm_log_analytics_workspace_container_app.id
  diagnostic_eventhub_name                = ""
  tags                                    = local.tags
}
