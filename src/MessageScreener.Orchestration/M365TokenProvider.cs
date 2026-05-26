using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace MessageScreener.Orchestration;

/// <summary>
/// Provides M365 access tokens for WorkIQ MCP integration.
/// Handles token refresh and secure storage of refresh tokens in Key Vault.
/// </summary>
public interface IM365TokenProvider
{
    /// <summary>
    /// Gets a valid M365 access token for API calls.
    /// Refreshes from stored refresh token if needed.
    /// </summary>
    ValueTask<string> GetM365AccessTokenAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Stores owner's M365 refresh token after OAuth2 auth flow.
    /// </summary>
    ValueTask StoreM365RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken);

    /// <summary>
    /// Revokes M365 auth by deleting stored refresh token.
    /// </summary>
    ValueTask RevokeM365AuthAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Checks if M365 auth is configured and valid.
    /// </summary>
    ValueTask<bool> HasValidM365AuthAsync(CancellationToken cancellationToken);
}

/// <summary>
/// M365 token refresh response from OAuth2 token endpoint.
/// </summary>
internal record M365TokenResponse(
    string AccessToken,
    string RefreshToken,
    int ExpiresIn,
    string TokenType = "Bearer");

public sealed class M365TokenProviderOptions
{
    public const string SectionName = "MessageScreener:M365Auth";

    public bool Enabled { get; init; }

    public string? ClientId { get; init; }

    public string? ClientSecret { get; init; }

    public string? TenantId { get; init; } = "common";

    /// <summary>
    /// Optional externally reachable API base URL used for OAuth callback URI construction.
    /// Example: https://messagescreener-api.contoso.com
    /// </summary>
    public string? PublicBaseUrl { get; init; }

    /// <summary>
    /// URI of Key Vault secret containing refresh token.
    /// Format: https://vault.azure.net/secrets/secret-name
    /// </summary>
    public string? RefreshTokenKeyVaultUri { get; init; }

    /// <summary>
    /// Key Vault URL (base). Secret is stored as {VaultUrl}/secrets/{SecretName}
    /// </summary>
    public string? KeyVaultUrl { get; init; }

    public string RefreshTokenSecretName { get; init; } = "m365-refresh-token";
}

public sealed class M365TokenProvider(
    IOptions<M365TokenProviderOptions> options,
    IHttpClientFactory httpClientFactory,
    ILogger<M365TokenProvider> logger) : IM365TokenProvider
{
    private const string TokenEndpoint = "https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token";
    private const string M365Scope = "https://graph.microsoft.com/.default";

    private string? _cachedAccessToken;
    private DateTimeOffset _cachedTokenExpiry = DateTimeOffset.MinValue;

    public async ValueTask<string> GetM365AccessTokenAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
        {
            M365TokenProviderLog.M365AuthDisabled(logger);
            throw new InvalidOperationException("M365 authentication is not enabled.");
        }

        // Return cached token if still valid (with 5-min buffer).
        if (!string.IsNullOrEmpty(_cachedAccessToken) && DateTimeOffset.UtcNow.AddMinutes(5) < _cachedTokenExpiry)
        {
            M365TokenProviderLog.TokenFromCache(logger);
            return _cachedAccessToken;
        }

        // Refresh token if cache is expired.
        string refreshToken = await GetStoredRefreshTokenAsync(cancellationToken);
        if (string.IsNullOrEmpty(refreshToken))
        {
            M365TokenProviderLog.NoRefreshToken(logger);
            throw new InvalidOperationException(
                "No M365 refresh token found. Owner must authenticate via /auth/m365/initiate first.");
        }

        M365TokenProviderLog.RefreshingToken(logger);
        M365TokenResponse tokenResponse = await ExchangeRefreshTokenAsync(refreshToken, cancellationToken);

        // Cache the new token.
        _cachedAccessToken = tokenResponse.AccessToken;
        _cachedTokenExpiry = DateTimeOffset.UtcNow.AddSeconds(tokenResponse.ExpiresIn);

        // Store the new refresh token (in case server issued a new one).
        await StoreRefreshTokenInKeyVaultAsync(tokenResponse.RefreshToken, cancellationToken);

        M365TokenProviderLog.TokenRefreshed(logger, _cachedTokenExpiry);
        return tokenResponse.AccessToken;
    }

    public async ValueTask StoreM365RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
        {
            throw new InvalidOperationException("M365 authentication is not enabled.");
        }

        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            throw new ArgumentException("Refresh token cannot be null or empty.", nameof(refreshToken));
        }

        await StoreRefreshTokenInKeyVaultAsync(refreshToken, cancellationToken);
        M365TokenProviderLog.RefreshTokenStored(logger);

        // Clear cache to force re-auth flow.
        _cachedAccessToken = null;
        _cachedTokenExpiry = DateTimeOffset.MinValue;
    }

    public async ValueTask RevokeM365AuthAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        try
        {
            SecretClient kv = GetKeyVaultClient();
            await kv.StartDeleteSecretAsync(options.Value.RefreshTokenSecretName, cancellationToken);

            _cachedAccessToken = null;
            _cachedTokenExpiry = DateTimeOffset.MinValue;

            M365TokenProviderLog.M365AuthRevoked(logger);
        }
        catch (Exception ex)
        {
            M365TokenProviderLog.RevokeError(logger, ex.Message);
            throw;
        }
    }

    public async ValueTask<bool> HasValidM365AuthAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
        {
            return false;
        }

        try
        {
            string refreshToken = await GetStoredRefreshTokenAsync(cancellationToken);
            return !string.IsNullOrEmpty(refreshToken);
        }
        catch
        {
            return false;
        }
    }

    private async ValueTask<string> GetStoredRefreshTokenAsync(CancellationToken cancellationToken)
    {
        try
        {
            SecretClient kv = GetKeyVaultClient();
            KeyVaultSecret secret = await kv.GetSecretAsync(
                options.Value.RefreshTokenSecretName,
                cancellationToken: cancellationToken);

            return secret.Value ?? string.Empty;
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            M365TokenProviderLog.RefreshTokenNotFound(logger);
            return string.Empty;
        }
    }

    private async ValueTask StoreRefreshTokenInKeyVaultAsync(string refreshToken, CancellationToken cancellationToken)
    {
        SecretClient kv = GetKeyVaultClient();
        await kv.SetSecretAsync(
            options.Value.RefreshTokenSecretName,
            refreshToken,
            cancellationToken);
    }

    private async ValueTask<M365TokenResponse> ExchangeRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken)
    {
        using HttpClient http = httpClientFactory.CreateClient();

        string tokenEndpoint = TokenEndpoint.Replace("{tenantId}", options.Value.TenantId);

        var request = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = options.Value.ClientId!,
            ["client_secret"] = options.Value.ClientSecret!,
            ["refresh_token"] = refreshToken,
            ["grant_type"] = "refresh_token",
            ["scope"] = M365Scope,
        });

        using HttpResponseMessage response = await http.PostAsync(tokenEndpoint, request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            string errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            M365TokenProviderLog.TokenExchangeFailed(logger, response.StatusCode, errorBody);
            throw new InvalidOperationException(
                $"Failed to refresh M365 token: {response.StatusCode}. {errorBody}");
        }

        string content = await response.Content.ReadAsStringAsync(cancellationToken);
        JsonElement json = JsonSerializer.Deserialize<JsonElement>(content);

        string accessToken = json.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("Token response missing access_token.");
        string newRefreshToken = json.TryGetProperty("refresh_token", out JsonElement rToken)
            ? (rToken.GetString() ?? refreshToken)
            : refreshToken;
        int expiresIn = json.GetProperty("expires_in").GetInt32();

        return new M365TokenResponse(accessToken, newRefreshToken, expiresIn);
    }

    private SecretClient GetKeyVaultClient()
    {
        string vaultUrl = options.Value.KeyVaultUrl
            ?? throw new InvalidOperationException("KeyVaultUrl is required when M365 auth is enabled.");

        if (!vaultUrl.EndsWith('/'))
        {
            vaultUrl += '/';
        }

        var credential = new DefaultAzureCredential();
        return new SecretClient(new Uri(vaultUrl), credential);
    }
}

/// <summary>
/// Logging helpers for M365TokenProvider.
/// </summary>
internal static partial class M365TokenProviderLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "M365 authentication is disabled.")]
    internal static partial void M365AuthDisabled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Returning cached M365 access token.")]
    internal static partial void TokenFromCache(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "No M365 refresh token stored.")]
    internal static partial void NoRefreshToken(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Refreshing M365 access token.")]
    internal static partial void RefreshingToken(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "M365 access token refreshed. Expiry: {ExpiresAt:O}")]
    internal static partial void TokenRefreshed(ILogger logger, DateTimeOffset expiresAt);

    [LoggerMessage(Level = LogLevel.Information, Message = "M365 refresh token stored in Key Vault.")]
    internal static partial void RefreshTokenStored(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "M365 authentication revoked.")]
    internal static partial void M365AuthRevoked(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to revoke M365 auth: {Message}")]
    internal static partial void RevokeError(ILogger logger, string message);

    [LoggerMessage(Level = LogLevel.Warning, Message = "M365 refresh token not found in Key Vault.")]
    internal static partial void RefreshTokenNotFound(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to exchange refresh token. Status: {StatusCode}. Body: {ErrorBody}")]
    internal static partial void TokenExchangeFailed(ILogger logger, System.Net.HttpStatusCode statusCode, string errorBody);
}
