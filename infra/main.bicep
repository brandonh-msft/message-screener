targetScope = 'subscription'

@minLength(1)
@maxLength(64)
@description('Name of the environment that can be used as part of naming resource convention')
param environmentName string

@minLength(1)
@description('Primary location for all resources')
param location string

@description('Optional Teams app ID used when generating Teams manifest output values.')
param teamsAppId string = ''

@description('Optional personal screener conversation ID used as the primary review-delivery target.')
param personalReviewConversationId string = ''

@secure()
@description('Optional GitHub token used by GitHub Copilot SDK runtime sessions in deployed container environments.')
param githubCopilotToken string = ''

@description('Optional GitHub Copilot model override for runtime reply drafting sessions.')
param copilotModel string = ''

@description('Optional GitHub Copilot agent override for runtime reply drafting sessions.')
param copilotAgent string = 'message-screener-researcher'

@description('Optional single-tenant Microsoft Entra application ID used for Copilot Studio skill registration.')
param skillAppId string = ''


param messagescreenerApiExists bool

// Tags that should be applied to all resources.
// 
// Note that 'azd-service-name' tags should be applied separately to service host resources.
// Example usage:
//   tags: union(tags, { 'azd-service-name': <service name in azure.yaml> })
var tags = {
  'azd-env-name': environmentName
}

// Organize resources in a resource group
resource rg 'Microsoft.Resources/resourceGroups@2021-04-01' = {
  name: 'rg-${environmentName}'
  location: location
  tags: tags
}

module resources 'resources.bicep' = {
  scope: rg
  name: 'resources'
  params: {
    environmentName: environmentName
    location: location
    tags: tags
    messagescreenerApiExists: messagescreenerApiExists
    teamsAppId: teamsAppId
    personalReviewConversationId: personalReviewConversationId
    githubCopilotToken: githubCopilotToken
    copilotModel: copilotModel
    copilotAgent: copilotAgent
    skillAppId: skillAppId
  }
}
output AZURE_CONTAINER_APPS_ENVIRONMENT_NAME string = resources.outputs.AZURE_CONTAINER_APPS_ENVIRONMENT_NAME
output AZURE_CONTAINER_REGISTRY_ENDPOINT string = resources.outputs.AZURE_CONTAINER_REGISTRY_ENDPOINT
output AZURE_CONTAINER_REGISTRY_NAME string = resources.outputs.AZURE_CONTAINER_REGISTRY_NAME
output AZURE_KEY_VAULT_ENDPOINT string = resources.outputs.AZURE_KEY_VAULT_ENDPOINT
output AZURE_KEY_VAULT_NAME string = resources.outputs.AZURE_KEY_VAULT_NAME
output AZURE_RESOURCE_MESSAGESCREENER_API_ID string = resources.outputs.AZURE_RESOURCE_MESSAGESCREENER_API_ID
output SERVICE_API_NAME string = resources.outputs.SERVICE_API_NAME
output SERVICE_API_URI string = resources.outputs.SERVICE_API_URI
output MESSAGE_SCREENER_PUBLIC_BASE_URL string = resources.outputs.MESSAGE_SCREENER_PUBLIC_BASE_URL
output MESSAGE_SCREENER_API_ENDPOINT string = resources.outputs.MESSAGE_SCREENER_API_ENDPOINT
output MESSAGE_SCREENER_TEAMS_APP_ID string = resources.outputs.MESSAGE_SCREENER_TEAMS_APP_ID
output MESSAGE_SCREENER_TEAMS_BOT_ID string = resources.outputs.MESSAGE_SCREENER_TEAMS_BOT_ID
output MESSAGE_SCREENER_MANAGED_IDENTITY_CLIENT_ID string = resources.outputs.MESSAGE_SCREENER_MANAGED_IDENTITY_CLIENT_ID
output MESSAGE_SCREENER_SKILL_APP_ID string = resources.outputs.MESSAGE_SCREENER_SKILL_APP_ID
output AZURE_LOG_ANALYTICS_WORKSPACE_NAME string = resources.outputs.AZURE_LOG_ANALYTICS_WORKSPACE_NAME
output AZURE_BOT_SERVICE_NAME string = resources.outputs.AZURE_BOT_SERVICE_NAME
