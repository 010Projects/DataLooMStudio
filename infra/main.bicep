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

@description('Creates API, worker, and web only after the explicit migration job has succeeded.')
param deployApplications bool = false

@description('Creates the manually invoked migration job only after its immutable image is published.')
param deployMigrationJob bool = false

@description('Live-verified migration execution evidence produced by scripts/Confirm-TestMigrationExecution.ps1.')
param migrationVerification object = {
  executionResourceId: ''
  executionName: ''
  status: ''
  imageDigest: ''
  lastAppliedMigration: ''
  evidenceSha256: ''
}

param location string = resourceGroup().location

param tags object = {
  workload: 'DataLooMStudio'
  artifact: 'DLS-ENG-NONPROD-TEST-ENVIRONMENT-PREPARATION-001'
  environment: environment
}

@description('Container image for the .NET API. Image publication authority is outside this checkpoint.')
param apiContainerImage string

@description('Container image for the React web application. Image publication authority is outside this checkpoint.')
param webContainerImage string

@description('Container image for the non-destructive background worker. Image publication authority is outside this checkpoint.')
param workerContainerImage string

@description('Container image for the explicitly invoked migration job.')
param migrationContainerImage string

@description('PostgreSQL administrator login used only for Development password bootstrap.')
param postgresAdministratorLogin string = ''

@secure()
@description('PostgreSQL administrator password supplied by deployment authority.')
param postgresAdministratorPassword string = ''

@description('Microsoft Entra authority used by API token validation. Required by production startup validation.')
param entraAuthority string = ''

@description('Single approved Microsoft Entra tenant GUID used for issuer and actor-tenant validation.')
param entraTenantId string = ''

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

@description('Delegated API scope required in access tokens, for example api://<api-client-id>/Dls.Access.')
param entraApiScope string = ''

@description('Public SPA application id used by the browser authorization-code-with-PKCE flow.')
param entraSpaClientId string = ''

@description('Private managed-identity malware scanner endpoint.')
param malwareScannerEndpoint string = ''

@description('Application ID URI used to acquire a managed-identity token for the malware scanner.')
param malwareScannerAudience string = ''

var normalized = toLower(replace(environmentName, '-', ''))
var suffix = uniqueString(resourceGroup().id, environmentName)
var apiName = '${environmentName}-api'
var webName = '${environmentName}-web'
var workerName = '${environmentName}-worker'
var migrationJobName = '${environmentName}-migrate'
var storageName = take('${normalized}${suffix}', 24)
var keyVaultName = take('${environmentName}-kv-${suffix}', 24)
var serviceBusName = take('${environmentName}-sb-${suffix}', 50)
var postgresName = take('${environmentName}-pg-${suffix}', 63)
var registryName = take('${normalized}acr${suffix}', 50)
var apiIdentityName = '${environmentName}-api-mi'
var workerIdentityName = '${environmentName}-worker-mi'
var migrationIdentityName = '${environmentName}-migration-mi'
var webIdentityName = '${environmentName}-web-mi'
var databaseName = 'dataloomstudio'
var outboxTopicName = 'dataloomstudio-outbox'
var evidenceContainerName = 'evidence'
var isHardenedEnvironment = contains([
  'test'
  'pilot'
  'prod'
], environment)
var aspNetCoreEnvironment = environment == 'test' ? 'Test' : (contains(['pilot', 'prod'], environment) ? 'Production' : 'Development')
var workerIdentitySubject = 'workload:dls-worker'
var apiDatabaseRoleName = apiIdentityName
var workerDatabaseRoleName = workerIdentityName
var migrationDatabaseRoleName = migrationIdentityName
var expectedMigrationExecutionResourceId = resourceId('Microsoft.App/jobs/executions', migrationJobName, migrationVerification.executionName)
var migrationVerificationValid = migrationVerification.status == 'Succeeded' && migrationVerification.imageDigest == migrationContainerImage && !empty(migrationVerification.executionName) && toLower(migrationVerification.executionResourceId) == toLower(expectedMigrationExecutionResourceId) && !empty(migrationVerification.lastAppliedMigration) && startsWith(migrationVerification.evidenceSha256, 'sha256:') && length(migrationVerification.evidenceSha256) == 71
var applicationDeploymentEnabled = deployApplications && deployMigrationJob && migrationVerificationValid

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
      {
        name: 'private-endpoints'
        properties: {
          addressPrefix: '10.40.3.0/24'
          privateEndpointNetworkPolicies: 'Disabled'
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

resource privateEndpointsSubnet 'Microsoft.Network/virtualNetworks/subnets@2024-05-01' existing = {
  parent: vnet
  name: 'private-endpoints'
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

resource blobPrivateDns 'Microsoft.Network/privateDnsZones@2024-06-01' = {
  name: 'privatelink.blob.${az.environment().suffixes.storage}'
  location: 'global'
  tags: tags
}

resource vaultPrivateDns 'Microsoft.Network/privateDnsZones@2024-06-01' = {
  name: 'privatelink.vaultcore.azure.net'
  location: 'global'
  tags: tags
}

resource serviceBusPrivateDns 'Microsoft.Network/privateDnsZones@2024-06-01' = {
  name: 'privatelink.servicebus.windows.net'
  location: 'global'
  tags: tags
}

resource registryPrivateDns 'Microsoft.Network/privateDnsZones@2024-06-01' = {
  name: 'privatelink.azurecr.io'
  location: 'global'
  tags: tags
}

module blobPrivateDnsLink './modules/private-dns-link.bicep' = {
  name: 'blob-private-dns-link'
  params: {
    dnsZoneName: blobPrivateDns.name
    virtualNetworkId: vnet.id
    linkName: '${environmentName}-blob-link'
  }
}

module vaultPrivateDnsLink './modules/private-dns-link.bicep' = {
  name: 'vault-private-dns-link'
  params: {
    dnsZoneName: vaultPrivateDns.name
    virtualNetworkId: vnet.id
    linkName: '${environmentName}-vault-link'
  }
}

module serviceBusPrivateDnsLink './modules/private-dns-link.bicep' = {
  name: 'servicebus-private-dns-link'
  params: {
    dnsZoneName: serviceBusPrivateDns.name
    virtualNetworkId: vnet.id
    linkName: '${environmentName}-servicebus-link'
  }
}

module registryPrivateDnsLink './modules/private-dns-link.bicep' = {
  name: 'registry-private-dns-link'
  params: {
    dnsZoneName: registryPrivateDns.name
    virtualNetworkId: vnet.id
    linkName: '${environmentName}-registry-link'
  }
}

resource migrationManagedIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: migrationIdentityName
  location: location
  tags: tags
}

resource webManagedIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: webIdentityName
  location: location
  tags: tags
}

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: registryName
  location: location
  tags: tags
  sku: {
    name: 'Premium'
  }
  properties: {
    adminUserEnabled: false
    dataEndpointEnabled: false
    publicNetworkAccess: isHardenedEnvironment ? 'Disabled' : 'Enabled'
    zoneRedundancy: environment == 'prod' ? 'Enabled' : 'Disabled'
    policies: {
      retentionPolicy: {
        days: 30
        status: 'enabled'
      }
      trustPolicy: {
        type: 'Notary'
        status: 'disabled'
      }
    }
  }
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
    publicNetworkAccess: isHardenedEnvironment ? 'Disabled' : 'Enabled'
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storage
  name: 'default'
  properties: {
    cors: {
      corsRules: isHardenedEnvironment ? [
        {
          allowedOrigins: split(allowedOriginsCsv, ';')
          allowedMethods: [
            'OPTIONS'
            'PUT'
          ]
          maxAgeInSeconds: 600
          exposedHeaders: [
            'ETag'
            'x-ms-request-id'
          ]
          allowedHeaders: [
            'content-type'
            'x-ms-blob-type'
            'x-ms-client-request-id'
            'x-ms-version'
          ]
        }
      ] : []
    }
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
    publicNetworkAccess: isHardenedEnvironment ? 'Disabled' : 'Enabled'
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
    publicNetworkAccess: isHardenedEnvironment ? 'Disabled' : 'Enabled'
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
  properties: union({
    authConfig: {
      activeDirectoryAuth: 'Enabled'
      passwordAuth: isHardenedEnvironment ? 'Disabled' : 'Enabled'
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
  }, isHardenedEnvironment ? {} : {
    administratorLogin: postgresAdministratorLogin
    administratorLoginPassword: postgresAdministratorPassword
  })
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

module postgresEntraAdmin './modules/postgres-entra-administrator.bicep' = {
  name: 'postgres-entra-migration-administrator'
  params: {
    administratorObjectId: migrationManagedIdentity.properties.principalId
    administratorPrincipalName: migrationIdentityName
    postgresServerName: postgres.name
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

var apiPostgresConnectionString = 'Host=${postgres.properties.fullyQualifiedDomainName};Port=5432;Database=${databaseName};Username=${apiDatabaseRoleName};Ssl Mode=Require;Trust Server Certificate=false'
var workerPostgresConnectionString = 'Host=${postgres.properties.fullyQualifiedDomainName};Port=5432;Database=${databaseName};Username=${workerDatabaseRoleName};Ssl Mode=Require;Trust Server Certificate=false'
var migrationPostgresConnectionString = 'Host=${postgres.properties.fullyQualifiedDomainName};Port=5432;Database=${databaseName};Username=${migrationDatabaseRoleName};Ssl Mode=Require;Trust Server Certificate=false'

resource apiApp 'Microsoft.App/containerApps@2024-03-01' = if (applicationDeploymentEnabled) {
  name: apiName
  location: location
  tags: union(tags, {
    migrationEvidence: migrationVerification.evidenceSha256
    migrationExecution: migrationVerification.executionName
  })
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
      registries: [
        {
          server: registry.properties.loginServer
          identity: apiManagedIdentity.id
        }
      ]
      ingress: {
        external: true
        targetPort: 8080
        transport: 'http'
      }
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
              value: apiPostgresConnectionString
            }
            {
              name: 'DataLooM__PostgreSqlUseManagedIdentity'
              value: 'true'
            }
            {
              name: 'AZURE_CLIENT_ID'
              value: apiManagedIdentity.properties.clientId
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
              name: 'EntraId__TenantId'
              value: entraTenantId
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
              name: 'EntraId__RequiredScope'
              value: last(split(entraApiScope, '/'))
            }
            {
              name: 'DataLooM__MalwareScannerEndpoint'
              value: malwareScannerEndpoint
            }
            {
              name: 'DataLooM__MalwareScannerAudience'
              value: malwareScannerAudience
            }
            {
              name: 'DataLooM__MalwareScannerTimeoutSeconds'
              value: '30'
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

resource workerApp 'Microsoft.App/containerApps@2024-03-01' = if (applicationDeploymentEnabled) {
  name: workerName
  location: location
  tags: union(tags, {
    migrationEvidence: migrationVerification.evidenceSha256
    migrationExecution: migrationVerification.executionName
  })
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
      registries: [
        {
          server: registry.properties.loginServer
          identity: workerManagedIdentity.id
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
              value: workerPostgresConnectionString
            }
            {
              name: 'DataLooM__PostgreSqlUseManagedIdentity'
              value: 'true'
            }
            {
              name: 'AZURE_CLIENT_ID'
              value: workerManagedIdentity.properties.clientId
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
              name: 'DataLooM__WorkerProcessingEnabled'
              value: isHardenedEnvironment ? 'true' : 'false'
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

module blobPrivateEndpoint './modules/private-endpoint.bicep' = {
  name: 'blob-private-endpoint'
  params: {
    name: '${environmentName}-blob-pe'
    location: location
    subnetId: privateEndpointsSubnet.id
    targetResourceId: storage.id
    groupId: 'blob'
    privateDnsZoneId: blobPrivateDns.id
    tags: tags
  }
}

module vaultPrivateEndpoint './modules/private-endpoint.bicep' = {
  name: 'vault-private-endpoint'
  params: {
    name: '${environmentName}-vault-pe'
    location: location
    subnetId: privateEndpointsSubnet.id
    targetResourceId: keyVault.id
    groupId: 'vault'
    privateDnsZoneId: vaultPrivateDns.id
    tags: tags
  }
}

module serviceBusPrivateEndpoint './modules/private-endpoint.bicep' = {
  name: 'servicebus-private-endpoint'
  params: {
    name: '${environmentName}-servicebus-pe'
    location: location
    subnetId: privateEndpointsSubnet.id
    targetResourceId: serviceBus.id
    groupId: 'namespace'
    privateDnsZoneId: serviceBusPrivateDns.id
    tags: tags
  }
}

module registryPrivateEndpoint './modules/private-endpoint.bicep' = {
  name: 'registry-private-endpoint'
  params: {
    name: '${environmentName}-registry-pe'
    location: location
    subnetId: privateEndpointsSubnet.id
    targetResourceId: registry.id
    groupId: 'registry'
    privateDnsZoneId: registryPrivateDns.id
    tags: tags
  }
}

resource migrationJob 'Microsoft.App/jobs@2024-03-01' = if (deployMigrationJob) {
  name: migrationJobName
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${migrationManagedIdentity.id}': {}
    }
  }
  properties: {
    environmentId: containerEnvironment.id
    configuration: {
      triggerType: 'Manual'
      replicaTimeout: 1800
      replicaRetryLimit: 0
      manualTriggerConfig: {
        parallelism: 1
        replicaCompletionCount: 1
      }
      registries: [
        {
          server: registry.properties.loginServer
          identity: migrationManagedIdentity.id
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'migrate'
          image: migrationContainerImage
          args: [
            '--apply'
            '--bootstrap-runtime-roles'
          ]
          env: [
            {
              name: 'DOTNET_ENVIRONMENT'
              value: aspNetCoreEnvironment
            }
            {
              name: 'AZURE_CLIENT_ID'
              value: migrationManagedIdentity.properties.clientId
            }
            {
              name: 'ConnectionStrings__DataLooM'
              value: migrationPostgresConnectionString
            }
            {
              name: 'DataLooM__PostgreSqlUseManagedIdentity'
              value: 'true'
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
              name: 'DataLooM__DatabaseRoles__ApiName'
              value: apiDatabaseRoleName
            }
            {
              name: 'DataLooM__DatabaseRoles__ApiObjectId'
              value: apiManagedIdentity.properties.principalId
            }
            {
              name: 'DataLooM__DatabaseRoles__WorkerName'
              value: workerDatabaseRoleName
            }
            {
              name: 'DataLooM__DatabaseRoles__WorkerObjectId'
              value: workerManagedIdentity.properties.principalId
            }
            {
              name: 'DLS_MIGRATION_IMAGE_REFERENCE'
              value: migrationContainerImage
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
    }
  }
  dependsOn: [
    postgresDatabase
    postgresEntraAdmin
  ]
}

resource webApp 'Microsoft.App/containerApps@2024-03-01' = if (applicationDeploymentEnabled) {
  name: webName
  location: location
  tags: union(tags, {
    migrationEvidence: migrationVerification.evidenceSha256
    migrationExecution: migrationVerification.executionName
  })
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${webManagedIdentity.id}': {}
    }
  }
  properties: {
    managedEnvironmentId: containerEnvironment.id
    configuration: {
      activeRevisionsMode: 'Single'
      registries: [
        {
          server: registry.properties.loginServer
          identity: webManagedIdentity.id
        }
      ]
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
          env: [
            {
              name: 'DLS_ENTRA_AUTHORITY'
              value: entraAuthority
            }
            {
              name: 'DLS_ENTRA_TENANT_ID'
              value: entraTenantId
            }
            {
              name: 'DLS_SPA_CLIENT_ID'
              value: entraSpaClientId
            }
            {
              name: 'DLS_API_SCOPE'
              value: entraApiScope
            }
            {
              name: 'DLS_API_ORIGIN'
              value: 'https://${apiApp!.properties.configuration.ingress.fqdn}'
            }
          ]
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

module apiEvidenceStorageRoles './modules/api-evidence-storage-roles.bicep' = {
  name: 'api-evidence-storage-roles'
  scope: subscription()
  params: {
    assignableScope: resourceGroup().id
    roleNamePrefix: environmentName
  }
}

resource apiBlobDelegationKeyIssuer 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, apiManagedIdentity.id, 'blob-delegation-key-issuer')
  scope: storage
  properties: {
    principalId: apiManagedIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: apiEvidenceStorageRoles.outputs.delegationRoleDefinitionId
  }
}

resource apiEvidenceBlobData 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(evidenceContainer.id, apiManagedIdentity.id, 'evidence-blob-data-non-delete')
  scope: evidenceContainer
  properties: {
    principalId: apiManagedIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: apiEvidenceStorageRoles.outputs.evidenceDataRoleDefinitionId
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

resource apiRegistryPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(registry.id, apiManagedIdentity.id, 'acr-pull')
  scope: registry
  properties: {
    principalId: apiManagedIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
  }
}

resource workerRegistryPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(registry.id, workerManagedIdentity.id, 'acr-pull')
  scope: registry
  properties: {
    principalId: workerManagedIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
  }
}

resource migrationRegistryPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(registry.id, migrationManagedIdentity.id, 'acr-pull')
  scope: registry
  properties: {
    principalId: migrationManagedIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
  }
}

resource webRegistryPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(registry.id, webManagedIdentity.id, 'acr-pull')
  scope: registry
  properties: {
    principalId: webManagedIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
  }
}

output apiUrl string = applicationDeploymentEnabled ? 'https://${apiApp!.properties.configuration.ingress.fqdn}' : ''
output webUrl string = applicationDeploymentEnabled ? 'https://${webApp!.properties.configuration.ingress.fqdn}' : ''
output workerContainerAppName string = applicationDeploymentEnabled ? workerApp!.name : ''
output migrationJobName string = deployMigrationJob ? migrationJob!.name : ''
output migrationManagedIdentityPrincipalId string = migrationManagedIdentity.properties.principalId
output apiManagedIdentityPrincipalId string = apiManagedIdentity.properties.principalId
output workerManagedIdentityPrincipalId string = workerManagedIdentity.properties.principalId
output postgresServerName string = postgres.name
output serviceBusNamespace string = serviceBus.name
output storageAccountName string = storage.name
output containerRegistryLoginServer string = registry.properties.loginServer
