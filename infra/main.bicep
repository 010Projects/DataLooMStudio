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

@description('Container image for the non-destructive background worker. Image publication authority is outside this checkpoint.')
param workerContainerImage string

@description('PostgreSQL administrator login used only for initial server bootstrap.')
param postgresAdministratorLogin string

@secure()
@description('PostgreSQL administrator password supplied by deployment authority.')
param postgresAdministratorPassword string

@description('Optional Microsoft Entra administrator principal display name for PostgreSQL.')
param entraAdministratorPrincipalName string = ''

@description('Optional Microsoft Entra administrator object id for PostgreSQL.')
param entraAdministratorObjectId string = ''

@description('Microsoft Entra authority used by API token validation. Required by production startup validation.')
param entraAuthority string = ''

@description('Microsoft Entra client/application id used by API token validation. Required by production startup validation when audience is not supplied.')
param entraClientId string = ''

@description('Microsoft Entra audience used by API token validation. Required by production startup validation when client id is not supplied.')
param entraAudience string = ''

@description('Externally governed host names for production API host filtering.')
param allowedHosts string = '*'

@description('Semicolon-delimited externally governed browser origins for production CORS.')
param allowedOriginsCsv string = ''

@description('OpenTelemetry collector endpoint. Required by production startup validation.')
param otelExporterOtlpEndpoint string = ''

var normalized = toLower(replace(environmentName, '-', ''))
var suffix = uniqueString(resourceGroup().id, environmentName)
var apiName = '${environmentName}-api'
var webName = '${environmentName}-web'
var workerName = '${environmentName}-worker'
var storageName = take('${normalized}${suffix}', 24)
var keyVaultName = take('${environmentName}-kv-${suffix}', 24)
var serviceBusName = take('${environmentName}-sb-${suffix}', 50)
var postgresName = take('${environmentName}-pg-${suffix}', 63)
var apiIdentityName = '${environmentName}-api-mi'
var workerIdentityName = '${environmentName}-worker-mi'
var databaseName = 'dataloomstudio'
var outboxTopicName = 'dataloomstudio-outbox'
var evidenceContainerName = 'evidence'
var aspNetCoreEnvironment = environment == 'prod' ? 'Production' : 'Development'
var workerIdentitySubject = 'workload:dls-worker'

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

resource apiManagedIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: apiIdentityName
  location: location
  tags: tags
}

resource workerManagedIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: workerIdentityName
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
      '${apiManagedIdentity.id}': {}
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
              name: 'ASPNETCORE_ENVIRONMENT'
              value: aspNetCoreEnvironment
            }
            {
              name: 'AllowedHosts'
              value: allowedHosts
            }
            {
              name: 'ConnectionStrings__DataLooM'
              secretRef: 'postgres-connection'
            }
            {
              name: 'DataLooM__EnvironmentName'
              value: environmentName
            }
            {
              name: 'DataLooM__EnvironmentKind'
              value: environment
            }
            {
              name: 'DataLooM__AllowedOriginsCsv'
              value: allowedOriginsCsv
            }
            {
              name: 'EntraId__Authority'
              value: entraAuthority
            }
            {
              name: 'EntraId__ClientId'
              value: entraClientId
            }
            {
              name: 'EntraId__Audience'
              value: entraAudience
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
            {
              name: 'OTEL_EXPORTER_OTLP_ENDPOINT'
              value: otelExporterOtlpEndpoint
            }
          ]
          probes: [
            {
              type: 'Liveness'
              httpGet: {
                path: '/healthz'
                port: 8080
                scheme: 'HTTP'
              }
              initialDelaySeconds: 30
              periodSeconds: 30
              timeoutSeconds: 5
              failureThreshold: 3
            }
            {
              type: 'Readiness'
              httpGet: {
                path: '/readyz'
                port: 8080
                scheme: 'HTTP'
              }
              initialDelaySeconds: 15
              periodSeconds: 15
              timeoutSeconds: 5
              failureThreshold: 3
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

resource workerApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: workerName
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${workerManagedIdentity.id}': {}
    }
  }
  properties: {
    managedEnvironmentId: containerEnvironment.id
    configuration: {
      activeRevisionsMode: 'Single'
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
          name: 'worker'
          image: workerContainerImage
          env: [
            {
              name: 'DOTNET_ENVIRONMENT'
              value: aspNetCoreEnvironment
            }
            {
              name: 'ConnectionStrings__DataLooM'
              secretRef: 'postgres-connection'
            }
            {
              name: 'DataLooM__EnvironmentName'
              value: environmentName
            }
            {
              name: 'DataLooM__EnvironmentKind'
              value: environment
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
            {
              name: 'DataLooM__WorkerIdentitySubject'
              value: workerIdentitySubject
            }
            {
              name: 'OTEL_EXPORTER_OTLP_ENDPOINT'
              value: otelExporterOtlpEndpoint
            }
          ]
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
        }
      ]
      scale: {
        minReplicas: environment == 'prod' ? 1 : 0
        maxReplicas: 5
      }
    }
  }
}

resource webApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: webName
  location: location
  tags: tags
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
          probes: [
            {
              type: 'Liveness'
              httpGet: {
                path: '/'
                port: 8080
                scheme: 'HTTP'
              }
              initialDelaySeconds: 30
              periodSeconds: 30
              timeoutSeconds: 5
              failureThreshold: 3
            }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 5
      }
    }
  }
}

resource apiStorageBlobContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, apiManagedIdentity.id, 'storage-blob-data-contributor')
  scope: storage
  properties: {
    principalId: apiManagedIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
  }
}

resource workerStorageBlobReader 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, workerManagedIdentity.id, 'storage-blob-data-reader')
  scope: storage
  properties: {
    principalId: workerManagedIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '2a2b9908-6ea1-4ae2-8e65-a410df84e7d1')
  }
}

resource apiServiceBusSender 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(serviceBus.id, apiManagedIdentity.id, 'service-bus-data-sender')
  scope: serviceBus
  properties: {
    principalId: apiManagedIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '69a216fc-b8fb-44d8-bc22-1f3c2cd27a39')
  }
}

resource workerServiceBusSender 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(serviceBus.id, workerManagedIdentity.id, 'service-bus-data-sender')
  scope: serviceBus
  properties: {
    principalId: workerManagedIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '69a216fc-b8fb-44d8-bc22-1f3c2cd27a39')
  }
}

resource workerServiceBusReceiver 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(serviceBus.id, workerManagedIdentity.id, 'service-bus-data-receiver')
  scope: serviceBus
  properties: {
    principalId: workerManagedIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4f6d3b9b-027b-4f4c-9142-0e5a2a2247e0')
  }
}

resource apiKeyVaultSecretsUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, apiManagedIdentity.id, 'key-vault-secrets-user')
  scope: keyVault
  properties: {
    principalId: apiManagedIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')
  }
}

resource workerKeyVaultSecretsUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, workerManagedIdentity.id, 'key-vault-secrets-user')
  scope: keyVault
  properties: {
    principalId: workerManagedIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')
  }
}

output apiUrl string = 'https://${apiApp.properties.configuration.ingress.fqdn}'
output webUrl string = 'https://${webApp.properties.configuration.ingress.fqdn}'
output workerContainerAppName string = workerApp.name
output postgresServerName string = postgres.name
output serviceBusNamespace string = serviceBus.name
output storageAccountName string = storage.name
