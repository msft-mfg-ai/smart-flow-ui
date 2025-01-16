param location string = resourceGroup().location

param existingVirtualNetworkName string = ''
param newVirtualNetworkName string = ''
param vnetAddressPrefix string
param subnet1Name string
param subnet2Name string
param subnet1Prefix string
param subnet2Prefix string

var useExistingResource = !empty(existingVirtualNetworkName)

resource existingVirtualNetwork 'Microsoft.Network/virtualNetworks@2024-01-01' existing = if (useExistingResource) {
  name: existingVirtualNetworkName
  resource subnet1 'subnets' existing = {
    name: subnet1Name
  }
  resource subnet2 'subnets' existing = {
    name: subnet2Name
  }
}

resource newVirtualNetwork 'Microsoft.Network/virtualNetworks@2024-01-01' = if (!useExistingResource) {
  name: newVirtualNetworkName
  location: location
  properties: {
    addressSpace: {
      addressPrefixes: [
        vnetAddressPrefix
      ]
    }
    subnets: [
      {
        name: subnet1Name
        properties: {
          addressPrefix: subnet1Prefix
        }
      }
      {
        name: subnet2Name
        properties: {
          addressPrefix: subnet2Prefix
        }
      }
    ]
  }

  resource subnet1 'subnets' existing = {
    name: subnet1Name
  }

  resource subnet2 'subnets' existing = {
    name: subnet2Name
  }
}

output vnetResourceId string = useExistingResource ? existingVirtualNetwork.id : newVirtualNetwork.id
output vnetName string = useExistingResource ? existingVirtualNetwork.name : newVirtualNetwork.name
output vnetAddressPrefix string = useExistingResource ? existingVirtualNetwork.properties.addressSpace.addressPrefixes[0] :  newVirtualNetwork.properties.addressSpace.addressPrefixes[0]
output subnet1ResourceId string = useExistingResource ? existingVirtualNetwork::subnet1.id : newVirtualNetwork::subnet1.id
output subnet2ResourceId string = useExistingResource ? existingVirtualNetwork::subnet2.id : newVirtualNetwork::subnet2.id
