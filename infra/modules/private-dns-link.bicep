targetScope = 'resourceGroup'

param dnsZoneName string
param virtualNetworkId string
param linkName string

resource dnsZone 'Microsoft.Network/privateDnsZones@2024-06-01' existing = {
  name: dnsZoneName
}

resource link 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2024-06-01' = {
  parent: dnsZone
  name: linkName
  location: 'global'
  properties: {
    registrationEnabled: false
    virtualNetwork: { id: virtualNetworkId }
  }
}
