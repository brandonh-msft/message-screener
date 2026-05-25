@description('The location used for all deployed resources')
param location string = resourceGroup().location

@description('Environment name used for deterministic resource naming and outputs.')
param environmentName string

@description('Tags that will be applied to all resources')
param tags object = {}

@description('Optional Teams app ID used by manifest generation outputs.')
param teamsAppId string = ''


param messagescreenerApiExists bool

var abbrs = loadJsonContent('./abbreviations.json')
var resourceToken = uniqueString(subscription().id, resourceGroup().id, location)
var registryName = '${abbrs.containerRegistryRegistries}${resourceToken}'
var containerAppsEnvironmentName = '${abbrs.appManagedEnvironments}${resourceToken}'
var workspaceName = '${abbrs.operationalInsightsWorkspaces}${resourceToken}'
var botServiceName = 'bot-${environmentName}-${take(resourceToken, 6)}'
var resolvedTeamsAppId = empty(teamsAppId) ? guid(resourceGroup().id, 'message-screener-teams-app') : teamsAppId

// Monitor application with Azure Monitor
module monitoring 'br/public:avm/ptn/azd/monitoring:0.1.0' = {
  name: 'monitoring'
  params: {
    logAnalyticsName: workspaceName
    applicationInsightsName: '${abbrs.insightsComponents}${resourceToken}'
    applicationInsightsDashboardName: '${abbrs.portalDashboards}${resourceToken}'
    location: location
    tags: tags
  }
}
// Container registry
module containerRegistry 'br/public:avm/res/container-registry/registry:0.1.1' = {
  name: 'registry'
  params: {
    name: registryName
    location: location
    tags: tags
    publicNetworkAccess: 'Enabled'
    roleAssignments:[
      {
        principalId: messagescreenerApiIdentity.outputs.principalId
        principalType: 'ServicePrincipal'
        roleDefinitionIdOrName: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
      }
    ]
  }
}

// Container apps environment
module containerAppsEnvironment 'br/public:avm/res/app/managed-environment:0.4.5' = {
  name: 'container-apps-environment'
  params: {
    logAnalyticsWorkspaceResourceId: monitoring.outputs.logAnalyticsWorkspaceResourceId
    name: containerAppsEnvironmentName
    location: location
    zoneRedundant: false
  }
}

module messagescreenerApiIdentity 'br/public:avm/res/managed-identity/user-assigned-identity:0.2.1' = {
  name: 'messagescreenerApiidentity'
  params: {
    name: '${abbrs.managedIdentityUserAssignedIdentities}messagescreenerApi-${resourceToken}'
    location: location
  }
}
module messagescreenerApiFetchLatestImage './modules/fetch-container-image.bicep' = {
  name: 'messagescreenerApi-fetch-image'
  params: {
    exists: messagescreenerApiExists
    name: 'messagescreener-api'
  }
}

module messagescreenerApi 'br/public:avm/res/app/container-app:0.8.0' = {
  name: 'messagescreenerApi'
  params: {
    name: 'messagescreener-api'
    ingressTargetPort: 8080
    scaleMinReplicas: 1
    scaleMaxReplicas: 10
    secrets: {
      secureList:  [
      ]
    }
    containers: [
      {
        image: messagescreenerApiFetchLatestImage.outputs.?containers[?0].?image ?? 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest'
        name: 'main'
        resources: {
          cpu: json('0.5')
          memory: '1.0Gi'
        }
        env: [
          {
            name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
            value: monitoring.outputs.applicationInsightsConnectionString
          }
          {
            name: 'AZURE_CLIENT_ID'
            value: messagescreenerApiIdentity.outputs.clientId
          }
          {
            name: 'MessageScreener__Teams__ManagedIdentityClientId'
            value: messagescreenerApiIdentity.outputs.clientId
          }
          {
            name: 'MessageScreener__GraphWebhook__ClientState'
            value: guid(resourceGroup().id, 'graph-webhook-client-state')
          }
          {
            name: 'PORT'
            value: '8080'
          }
        ]
      }
    ]
    managedIdentities:{
      systemAssigned: false
      userAssignedResourceIds: [messagescreenerApiIdentity.outputs.resourceId]
    }
    registries:[
      {
        server: containerRegistry.outputs.loginServer
        identity: messagescreenerApiIdentity.outputs.resourceId
      }
    ]
    environmentResourceId: containerAppsEnvironment.outputs.resourceId
    location: location
    tags: union(tags, { 'azd-service-name': 'messagescreener-api' })
  }
}

resource messagescreenerApiResource 'Microsoft.App/containerApps@2024-03-01' existing = {
  name: 'messagescreener-api'
}

resource botService 'Microsoft.BotService/botServices@2022-09-15' = {
  name: botServiceName
  location: 'global'
  tags: tags
  kind: 'azurebot'
  sku: {
    name: 'F0'
  }
  properties: {
    displayName: 'Message Screener Bot (${environmentName})'
    description: 'Bot registration for Message Screener Teams app.'
    endpoint: 'https://${messagescreenerApiResource.properties.configuration.ingress.fqdn}/api/messages'
    msaAppId: messagescreenerApiIdentity.outputs.clientId
    msaAppTenantId: tenant().tenantId
    msaAppType: 'UserAssignedMSI'
    msaAppMSIResourceId: messagescreenerApiIdentity.outputs.resourceId
    isStreamingSupported: false
    publicNetworkAccess: 'Enabled'
  }
  dependsOn: [
    messagescreenerApi
  ]
}

resource botTeamsChannel 'Microsoft.BotService/botServices/channels@2022-09-15' = {
  parent: botService
  name: 'MsTeamsChannel'
  location: 'global'
  properties: {
    channelName: 'MsTeamsChannel'
  }
}

output AZURE_CONTAINER_APPS_ENVIRONMENT_NAME string = containerAppsEnvironmentName
output AZURE_CONTAINER_REGISTRY_ENDPOINT string = containerRegistry.outputs.loginServer
output AZURE_CONTAINER_REGISTRY_NAME string = registryName
output AZURE_RESOURCE_MESSAGESCREENER_API_ID string = messagescreenerApi.outputs.resourceId
output SERVICE_API_NAME string = 'messagescreener-api'
output SERVICE_API_URI string = 'https://${messagescreenerApiResource.properties.configuration.ingress.fqdn}'
output MESSAGE_SCREENER_PUBLIC_BASE_URL string = 'https://${messagescreenerApiResource.properties.configuration.ingress.fqdn}'
output MESSAGE_SCREENER_API_ENDPOINT string = 'https://${messagescreenerApiResource.properties.configuration.ingress.fqdn}'
output MESSAGE_SCREENER_TEAMS_APP_ID string = resolvedTeamsAppId
output MESSAGE_SCREENER_TEAMS_BOT_ID string = messagescreenerApiIdentity.outputs.clientId
output MESSAGE_SCREENER_MANAGED_IDENTITY_CLIENT_ID string = messagescreenerApiIdentity.outputs.clientId
output AZURE_LOG_ANALYTICS_WORKSPACE_NAME string = workspaceName
output AZURE_BOT_SERVICE_NAME string = botService.name
