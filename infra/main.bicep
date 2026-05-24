targetScope = 'resourceGroup'

@description('Environment name used to derive resource names.')
param environmentName string

@description('Azure region for all resources.')
param location string = resourceGroup().location

@description('Optional tags applied to all resources.')
param tags object = {}

@description('Teams app ID used for manifest generation output.')
param teamsAppId string = ''

@description('Teams bot ID (AAD app/client ID) used for manifest generation output.')
param teamsBotId string = ''

var suffix = toLower(uniqueString(resourceGroup().id, environmentName))
var acrName = 'acr${take(replace(environmentName, '-', ''), 15)}${take(suffix, 8)}'
var logAnalyticsName = 'log-${environmentName}-${take(suffix, 6)}'
var containerAppsEnvironmentName = 'cae-${environmentName}'
var userAssignedIdentityName = 'id-${environmentName}-app'
var containerAppName = 'api-${environmentName}'

resource userAssignedIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: userAssignedIdentityName
  location: location
  tags: tags
}

resource logAnalyticsWorkspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: logAnalyticsName
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
    features: {
      enableLogAccessUsingOnlyResourcePermissions: true
    }
  }
}

resource containerRegistry 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: acrName
  location: location
  tags: tags
  sku: {
    name: 'Basic'
  }
  properties: {
    adminUserEnabled: false
    publicNetworkAccess: 'Enabled'
  }
}

resource containerAppsEnvironment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: containerAppsEnvironmentName
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalyticsWorkspace.properties.customerId
        sharedKey: logAnalyticsWorkspace.listKeys().primarySharedKey
      }
    }
  }
}

resource containerApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: containerAppName
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${userAssignedIdentity.id}': {}
    }
  }
  properties: {
    managedEnvironmentId: containerAppsEnvironment.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
      }
      registries: [
        {
          server: containerRegistry.properties.loginServer
          identity: userAssignedIdentity.id
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'api'
          image: '${containerRegistry.properties.loginServer}/message-screener-api:latest'
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            {
              name: 'MessageScreener__Teams__ManagedIdentityClientId'
              value: userAssignedIdentity.properties.clientId
            }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 3
      }
    }
  }
}

resource acrPullAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(containerRegistry.id, userAssignedIdentity.properties.principalId, 'acr-pull')
  scope: containerRegistry
  properties: {
    principalId: userAssignedIdentity.properties.principalId
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
    principalType: 'ServicePrincipal'
  }
}

output AZURE_CONTAINER_APPS_ENVIRONMENT_NAME string = containerAppsEnvironment.name
output AZURE_CONTAINER_REGISTRY_ENDPOINT string = containerRegistry.properties.loginServer
output AZURE_CONTAINER_REGISTRY_NAME string = containerRegistry.name
output SERVICE_API_NAME string = containerApp.name
output SERVICE_API_URI string = 'https://${containerApp.properties.configuration.ingress.fqdn}'

output MESSAGE_SCREENER_PUBLIC_BASE_URL string = 'https://${containerApp.properties.configuration.ingress.fqdn}'
output MESSAGE_SCREENER_API_ENDPOINT string = 'https://${containerApp.properties.configuration.ingress.fqdn}'
output MESSAGE_SCREENER_TEAMS_APP_ID string = teamsAppId
output MESSAGE_SCREENER_TEAMS_BOT_ID string = teamsBotId

output MESSAGE_SCREENER_MANAGED_IDENTITY_CLIENT_ID string = userAssignedIdentity.properties.clientId
output AZURE_LOG_ANALYTICS_WORKSPACE_NAME string = logAnalyticsWorkspace.name
