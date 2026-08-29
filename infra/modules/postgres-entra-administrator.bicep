targetScope = 'resourceGroup'

param postgresServerName string
param administratorObjectId string
param administratorPrincipalName string
param tenantId string

resource postgres 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' existing = {
  name: postgresServerName
}

resource administrator 'Microsoft.DBforPostgreSQL/flexibleServers/administrators@2024-08-01' = {
  parent: postgres
  name: administratorObjectId
  properties: {
    principalName: administratorPrincipalName
    principalType: 'ServicePrincipal'
    tenantId: tenantId
  }
}

output administratorObjectId string = administrator.name
