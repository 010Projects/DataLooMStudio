targetScope = 'subscription'

@description('Resource group scope within which the API Evidence storage roles may be assigned.')
param assignableScope string

@description('Stable environment-qualified prefix for custom role display names.')
param roleNamePrefix string

var delegationRoleName = '${roleNamePrefix} API Blob Delegation Key Issuer'
var evidenceDataRoleName = '${roleNamePrefix} API Evidence Blob Data'

resource delegationRole 'Microsoft.Authorization/roleDefinitions@2022-04-01' = {
  name: guid(subscription().id, delegationRoleName)
  properties: {
    roleName: delegationRoleName
    description: 'Allows the DataLooM Studio API to issue user-delegation upload authority without container or Blob deletion rights.'
    type: 'CustomRole'
    permissions: [
      {
        actions: [
          'Microsoft.Storage/storageAccounts/read'
          'Microsoft.Storage/storageAccounts/blobServices/containers/read'
          'Microsoft.Storage/storageAccounts/blobServices/generateUserDelegationKey/action'
        ]
        notActions: []
        dataActions: []
        notDataActions: []
      }
    ]
    assignableScopes: [
      assignableScope
    ]
  }
}

resource evidenceDataRole 'Microsoft.Authorization/roleDefinitions@2022-04-01' = {
  name: guid(subscription().id, evidenceDataRoleName)
  properties: {
    roleName: evidenceDataRoleName
    description: 'Allows create/read/quarantine operations only for Evidence Blobs; destructive Blob operations are intentionally absent.'
    type: 'CustomRole'
    permissions: [
      {
        actions: []
        notActions: []
        dataActions: [
          'Microsoft.Storage/storageAccounts/blobServices/containers/blobs/read'
          'Microsoft.Storage/storageAccounts/blobServices/containers/blobs/write'
          'Microsoft.Storage/storageAccounts/blobServices/containers/blobs/tags/read'
          'Microsoft.Storage/storageAccounts/blobServices/containers/blobs/tags/write'
        ]
        notDataActions: []
      }
    ]
    assignableScopes: [
      assignableScope
    ]
  }
}

output delegationRoleDefinitionId string = delegationRole.id
output evidenceDataRoleDefinitionId string = evidenceDataRole.id
