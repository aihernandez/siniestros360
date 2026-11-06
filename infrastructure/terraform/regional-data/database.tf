resource "random_password" "service" {
  for_each = local.services
  length   = 40
  special  = false
}
resource "postgresql_role" "service" {
  for_each            = local.services
  name                = "s360_${each.key}"
  login               = true
  password            = random_password.service[each.key].result
  create_database     = false
  create_role         = false
  superuser           = false
  replication         = false
  skip_reassign_owned = true
}
resource "postgresql_grant" "public_database" {
  for_each    = local.services
  database    = local.region.database_names[each.key]
  role        = "public"
  object_type = "database"
  privileges  = []
}
resource "postgresql_grant" "service_database" {
  for_each    = local.services
  database    = local.region.database_names[each.key]
  role        = postgresql_role.service[each.key].name
  object_type = "database"
  privileges  = ["CONNECT", "CREATE", "TEMPORARY"]
  depends_on  = [postgresql_grant.public_database]
}
resource "postgresql_grant" "public_schema" {
  for_each    = local.services
  database    = local.region.database_names[each.key]
  role        = "public"
  object_type = "schema"
  schema      = "public"
  privileges  = []
}
resource "postgresql_grant" "service_schema" {
  for_each    = local.services
  database    = local.region.database_names[each.key]
  role        = postgresql_role.service[each.key].name
  object_type = "schema"
  schema      = "public"
  privileges  = ["USAGE", "CREATE"]
  depends_on  = [postgresql_grant.public_schema]
}
resource "postgresql_extension" "spatial" {
  name     = "postgis"
  database = local.region.database_names["location"]
  schema   = "public"
}
