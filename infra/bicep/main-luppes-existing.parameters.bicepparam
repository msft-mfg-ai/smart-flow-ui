// using 'main-basic.bicep'

// @description('ID of the service principal that will be granted access to the Key Vault')
// param principalId = 'xxxxxx-xxxx-xxxx-xxxxx-xxxxxxxxxx'

// @description('If you have an existing Cog Services Account, provide the name here')
// param existingCogServicesName = 'cog-fuwyp7kyt7kmy'
// param existingCogServicesResourceGroup  = 'rg-copilot-lll'

// @description('If you have an existing Container Registry Account, provide the name here')
// param existingContainerRegistryName = 'crfuwyp7kyt7kmy'
// param existingContainerRegistryResourceGroup = 'rg-copilot-lll'

// param environmentName = 'CI'
// param location = 'westus' // 'eastus2'
// param backendExists = false
// param backendDefinition = {
//   settings: []
// }
// param appContainerAppEnvironmentWorkloadProfileName = 'app'
// param containerAppEnvironmentWorkloadProfiles = [
//   {
//     name: 'app'
//     workloadProfileType: 'D4'
//     minimumCount: 1
//     maximumCount: 10
//   }
// ]

// param useManagedIdentityResourceAccess = true
// param azureChatGptStandardDeploymentName = 'chat'
// param azureEmbeddingDeploymentName = 'text-embedding'
