targetScope = 'resourceGroup'

@description('Short environment name used for globally unique resource names.')
@minLength(3)
@maxLength(24)
param environmentName string

@allowed([
  'dev'
  'test'
  'pilot'
  'prod'
])
param environment string = 'dev'

param location string = resourceGroup().location

param tags object = {
  workload: 'DataLooMStudio'
  artifact: 'DLS-ENG-FOUNDATION-001'
  environment: environment
}

@description('Container image for the .NET API. Image publication authority is outside this checkpoint.')
param apiContainerImage string

@description('Container image for the React web application. Image publication authority is outside this checkpoint.')
param webContainerImage string

@description('PostgreSQL administrator login used only for initial server bootstrap.')
param postgresAdministratorLogin string

@secure()
@description('PostgreSQL administrator password supplied by deployment authority.')
param postgresAdministratorPassword string

@description('Optional Microsoft Entra administrator principal display name for PostgreSQL.')
param entraAdministratorPrincipalName string = ''

@description('Optional Microsoft Entra administrator object id for PostgreSQL.')
param entraAdministratorObjectId string = ''

var normalized = toLower(replace(environmentName, '-', ''))
var suffix = uniqueString(resourceGroup().id, environmentName)
var apiName = '${environmentName}-api'
var webName = '${environmentName}-web'
var storageName = take('${normalized}${suffix}', 24)
var keyVaultName = take('${environmentName}-kv-${suffix}', 24)
var serviceBusName = take('${environmentName}-sb-${suffix}', 50)
var postgresName = take('${environmentName}-pg-${suffix}', 63)
var identityName = '${environmentName}-app-mi'
var databaseName = 'dataloomstudio'
var outboxTopicName = 'dataloomstudio-outbox'
var evidenceContainerName = 'evidence'

resource vnet 'Microsoft.Network/virtualNetworks@2024-05-01' = {
  name: '${environmentName}-vnet'
  location: location
  tags: tags
  properties: {
    addressSpace: {
      addressPrefixes: [
        '10.40.0.0/16'
      ]
    }
    subnets: [
      {
        name: 'container-apps'
        properties: {
          addressPrefix: '10.40.0.0/23'
          delegations: [
            {
              name: 'container-apps-delegation'
              properties: {
                serviceName: 'Microsoft.App/environments'
              }
            }
          ]
        }
      }
      {
        name: 'postgres'
        properties: {
          addressPrefix: '10.40.2.0/24'
          delegations: [
            {
              name: 'postgres-flexible-server-delegation'
              properties: {
                serviceName: 'Microsoft.DBforPostgreSQL/flexibleServers'
              }
            }
          ]
        }
      }
    ]
  }
}

resource containerAppsSubnet 'Microsoft.Network/virtualNetworks/subnets@2024-05-01' existing = {
  parent: vnet
  name: 'container-apps'
}

resource postgresSubnet 'Microsoft.Network/virtualNetworks/subnets@2024-05-01' existing = {
  parent: vnet
  name: 'postgres'
}

resource postgresPrivateDns 'Microsoft.Network/privateDnsZones@2024-06-01' = {
  name: 'privatelink.postgres.database.azure.com'
  location: 'global'
  tags: tags
}

resource postgresPrivateDnsLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2024-06-01' = {
  parent: postgresPrivateDns
  name: '${environmentName}-postgres-link'
  location: 'global'
  properties: {
    registrationEnabled: false
    virtualNetwork: {
      id: vnet.id
    }
  }
}

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${environmentName}-law'
  location: location
  tags: tags
  properties: {
    retentionInDays: 90
    sku: {
      name: 'PerGB2018'
    }
  }
}

resource managedIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: identityName
  location: location
  tags: tags
}

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageName
  location: location
  tags: tags
  sku: {
    name: 'Standard_ZRS'
  }
  kind: 'StorageV2'
  properties: {
    accessTier: 'Hot'
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false
    defaultToOAuthAuthentication: true
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storage
  name: 'default'
  properties: {
    deleteRetentionPolicy: {
      enabled: true
      days: 30
    }
    isVersioningEnabled: true
  }
}

resource evidenceContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: evidenceContainerName
  properties: {
    publicAccess: 'None'
  }
}

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultName
  location: location
  tags: tags
  properties: {
    tenantId: tenant().tenantId
    enablePurgeProtection: true
    enableRbacAuthorization: true
    enableSoftDelete: true
    sku: {
      family: 'A'
      name: 'standard'
    }
  }
}

resource serviceBus 'Microsoft.ServiceBus/namespaces@2024-01-01' = {
  name: serviceBusName
  location: location
  tags: tags
  sku: {
    name: 'Premium'
    tier: 'Premium'
    capacity: 1
  }
  properties: {
    disableLocalAuth: true
    zoneRedundant: environment == 'prod'
  }
}

resource outboxTopic 'Microsoft.ServiceBus/namespaces/topics@2024-01-01' = {
  parent: serviceBus
  name: outboxTopicName
  properties: {
    defaultMessageTimeToLive: 'P14D'
    duplicateDetectionHistoryTimeWindow: 'PT10M'
    enableBatchedOperations: true
    enablePartitioning: false
    requiresDuplicateDetection: true
    supportOrdering: true
  }
}

resource outboxSubscription 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = {
  parent: outboxTopic
  name: 'foundation-dispatcher'
  properties: {
    deadLetteringOnMessageExpiration: true
    defaultMessageTimeToLive: 'P14D'
    lockDuration: 'PT1M'
    maxDeliveryCount: 10
    requiresSession: false
  }
}

resource postgres 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' = {
  name: postgresName
  location: location
  tags: tags
  sku: {
    name: 'Standard_D2ds_v5'
    tier: 'GeneralPurpose'
  }
  properties: {
    administratorLogin: postgresAdministratorLogin
    administratorLoginPassword: postgresAdministratorPassword
    authConfig: {
      activeDirectoryAuth: 'Enabled'
      passwordAuth: 'Enabled'
      tenantId: tenant().tenantId
    }
    backup: {
      backupRetentionDays: 35
      geoRedundantBackup: 'Disabled'
    }
    highAvailability: {
      mode: environment == 'prod' ? 'ZoneRedundant' : 'Disabled'
    }
    network: {
      delegatedSubnetResourceId: postgresSubnet.id
      privateDnsZoneArmResourceId: postgresPrivateDns.id
      publicNetworkAccess: 'Disabled'
    }
    storage: {
      autoGrow: 'Enabled'
      storageSizeGB: 128
    }
    version: '18'
  }
  dependsOn: [
    postgresPrivateDnsLink
  ]
}

resource postgresDatabase 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2024-08-01' = {
  parent: postgres
  name: databaseName
  properties: {
    charset: 'UTF8'
    collation: 'en_US.utf8'
  }
}

resource postgresEntraAdmin 'Microsoft.DBforPostgreSQL/flexibleServers/administrators@2024-08-01' = if (!empty(entraAdministratorObjectId) && !empty(entraAdministratorPrincipalName)) {
  parent: postgres
  name: entraAdministratorObjectId
  properties: {
    principalName: entraAdministratorPrincipalName
    principalType: 'User'
    tenantId: tenant().tenantId
  }
}

resource containerEnvironment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: '${environmentName}-aca'
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalytics.properties.customerId
        sharedKey: logAnalytics.listKeys().primarySharedKey
      }
    }
    vnetConfiguration: {
      infrastructureSubnetId: containerAppsSubnet.id
    }
  }
}

var postgresConnectionString = 'Host=${postgres.properties.fullyQualifiedDomainName};Port=5432;Database=${databaseName};Username=${postgresAdministratorLogin};Password=${postgresAdministratorPassword};Ssl Mode=Require;Trust Server Certificate=false'

resource apiApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: apiName
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${managedIdentity.id}': {}
    }
  }
  properties: {
    managedEnvironmentId: containerEnvironment.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 8080
        transport: 'http'
      }
      secrets: [
        {
          name: 'postgres-connection'
          value: postgresConnectionString
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'api'
          image: apiContainerImage
          env: [
            {
              name: 'ConnectionStrings__DataLooM'
              secretRef: 'postgres-connection'
            }
            {
              name: 'DataLooM__BlobServiceUri'
              value: 'https://${storage.name}.blob.${az.environment().suffixes.storage}'
            }
            {
              name: 'DataLooM__EvidenceContainerName'
              value: evidenceContainer.name
            }
            {
              name: 'DataLooM__ServiceBusFullyQualifiedNamespace'
              value: '${serviceBus.name}.servicebus.windows.net'
            }
            {
              name: 'DataLooM__ServiceBusOutboxTopic'
              value: outboxTopic.name
            }
            {
              name: 'DataLooM__KeyVaultUri'
              value: keyVault.properties.vaultUri
            }
          ]
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
        }
      ]
      scale: {
        minReplicas: environment == 'prod' ? 2 : 1
        maxReplicas: 10
      }
    }
  }
}

resource webApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: webName
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${managedIdentity.id}': {}
    }
  }
  properties: {
    managedEnvironmentId: containerEnvironment.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 8080
        transport: 'http'
      }
    }
    template: {
      containers: [
        {
          name: 'web'
          image: webContainerImage
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 5
      }
    }
  }
}

resource storageBlobContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, managedIdentity.id, 'storage-blob-data-contributor')
  scope: storage
  properties: {
    principalId: managedIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
  }
}

resource serviceBusSender 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(serviceBus.id, managedIdentity.id, 'service-bus-data-sender')
  scope: serviceBus
  properties: {
    principalId: managedIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '69a216fc-b8fb-44d8-bc22-1f3c2cd27a39')
  }
}

resource keyVaultSecretsUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, managedIdentity.id, 'key-vault-secrets-user')
  scope: keyVault
  properties: {
    principalId: managedIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')
  }
}

output apiUrl string = 'https://${apiApp.properties.configuration.ingress.fqdn}'
output webUrl string = 'https://${webApp.properties.configuration.ingress.fqdn}'
output postgresServerName string = postgres.name
output serviceBusNamespace string = serviceBus.name
output storageAccountName string = storage.name
