using MessageScreener.Orchestration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MessageScreener.Api.Controllers;

/// <summary>
/// Handles M365 OAuth2 authentication flow for WorkIQ MCP integration.
/// Uses authorization code flow with PKCE and secure refresh token storage.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public sealed class AuthM365Controller(
    IM365TokenProvider m365TokenProvider,
    IHttpClientFactory httpClientFactory,
    IMemoryCache pkceStateCache,
    ILogger<AuthM365Controller> logger) : ControllerBase
{
    private const string AuthorizeEndpoint = "https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/authorize";
    private const string TokenEndpoint = "https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token";
    private const string M365Scope = "offline_access Mail.Read";
    private const string PkceStateCachePrefix = "m365-auth-state:";
    private const int PkceStateTtlSeconds = 900;

    /// <summary>
    /// Starts browser-based authorization code + PKCE flow.
    /// Returns an authorization URL the owner opens to complete sign-in.
    /// </summary>
    [HttpPost("start")]
    [ProducesResponseType(typeof(M365AuthStartResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public IActionResult StartAsync([FromServices] IOptions<M365TokenProviderOptions> options)
    {
        if (TryCreateM365ConfigurationProblem(options.Value, out ProblemDetails? configProblem))
        {
            return BadRequest(configProblem);
        }

        try
        {
            M365AuthStartResponse response = BuildStartResponse(options.Value);
            AuthM365ControllerLog.AuthStartCreated(logger);
            return Ok(response);
        }
        catch (Exception ex)
        {
            AuthM365ControllerLog.StartException(logger, ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "Authorization Start Failed",
                Detail = $"An error occurred: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Convenience browser endpoint that redirects directly to Entra authorize URL.
    /// </summary>
    [HttpGet("start")]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public IActionResult StartRedirectAsync([FromServices] IOptions<M365TokenProviderOptions> options)
    {
        if (TryCreateM365ConfigurationProblem(options.Value, out ProblemDetails? configProblem))
        {
            return BadRequest(configProblem);
        }

        M365AuthStartResponse response = BuildStartResponse(options.Value);
        AuthM365ControllerLog.AuthStartRedirect(logger);
        return Redirect(response.AuthorizationUrl);
    }

    /// <summary>
    /// OAuth callback endpoint for authorization code + PKCE.
    /// Exchanges code for refresh token and stores it in Key Vault.
    /// </summary>
    [HttpGet("callback")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> CallbackAsync(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        [FromQuery(Name = "error_description")] string? errorDescription,
        [FromServices] IOptions<M365TokenProviderOptions> options,
        CancellationToken cancellationToken)
    {
        if (TryCreateM365ConfigurationProblem(options.Value, out ProblemDetails? configProblem))
        {
            return BadRequest(configProblem);
        }

        if (!string.IsNullOrWhiteSpace(error))
        {
            AuthM365ControllerLog.CallbackError(logger, error, errorDescription ?? string.Empty);
            return BadRequest(new ProblemDetails
            {
                Title = "Authorization Denied",
                Detail = $"OAuth error: {error}. {errorDescription}"
            });
        }

        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid Callback",
                Detail = "Missing required query parameters: code and state."
            });
        }

        string cacheKey = BuildPkceStateCacheKey(state);
        if (!pkceStateCache.TryGetValue(cacheKey, out PkceAuthState? pkceState) || pkceState is null)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid or Expired State",
                Detail = "The auth state is missing or expired. Start authentication again."
            });
        }

        pkceStateCache.Remove(cacheKey);

        try
        {
            using HttpClient http = httpClientFactory.CreateClient();

            var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = options.Value.ClientId!,
                ["client_secret"] = options.Value.ClientSecret!,
                ["code"] = code,
                ["grant_type"] = "authorization_code",
                ["redirect_uri"] = pkceState.RedirectUri,
                ["code_verifier"] = pkceState.CodeVerifier,
                ["scope"] = M365Scope,
            });

            string tokenEndpoint = TokenEndpoint.Replace("{tenantId}", options.Value.TenantId ?? "common");
            using HttpResponseMessage response = await http.PostAsync(tokenEndpoint, tokenRequest, cancellationToken);

            string content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                AuthM365ControllerLog.TokenExchangeFailed(logger, response.StatusCode, content);
                return StatusCode((int)response.StatusCode, new ProblemDetails
                {
                    Title = "Token Exchange Failed",
                    Detail = $"Failed to exchange authorization code: {response.StatusCode}"
                });
            }

            JsonElement json = JsonSerializer.Deserialize<JsonElement>(content);
            string refreshToken = json.GetProperty("refresh_token").GetString()
                ?? throw new InvalidOperationException("Missing refresh_token in token response.");

            await m365TokenProvider.StoreM365RefreshTokenAsync(refreshToken, cancellationToken);
            AuthM365ControllerLog.TokensObtained(logger);

            const string successHtml = """
<!DOCTYPE html>
<html>
  <head><title>Message Screener Auth Complete</title></head>
  <body style="font-family:Segoe UI,Arial,sans-serif;padding:24px;">
    <h2>Authentication complete</h2>
    <p>WorkIQ can now access your M365 data for Message Screener.</p>
    <p>You can close this window and return to the terminal.</p>
  </body>
</html>
""";

            return Content(successHtml, "text/html");
        }
        catch (Exception ex)
        {
            AuthM365ControllerLog.CallbackException(logger, ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "Authorization Callback Failed",
                Detail = $"An error occurred: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Revokes M365 authentication and deletes stored refresh token.
    /// </summary>
    [HttpPost("revoke")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> RevokeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await m365TokenProvider.RevokeM365AuthAsync(cancellationToken);
            return NoContent();
        }
        catch (Exception ex)
        {
            AuthM365ControllerLog.RevokeException(logger, ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "Revocation Failed",
                Detail = $"An error occurred: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Checks if M365 authentication is configured.
    /// </summary>
    [HttpGet("status")]
    [ProducesResponseType(typeof(M365AuthStatusResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> StatusAsync(
        [FromServices] IOptions<M365TokenProviderOptions> options,
        CancellationToken cancellationToken)
    {
        bool hasConfigError = TryCreateM365ConfigurationProblem(options.Value, out ProblemDetails? configProblem);
        bool hasAuth = await m365TokenProvider.HasValidM365AuthAsync(cancellationToken);

        return Ok(new M365AuthStatusResponse(
            IsConfigured: hasAuth,
            Message: hasAuth
                ? "M365 authentication is configured. WorkIQ can access your data."
                : hasConfigError
                    ? configProblem!.Detail ?? "M365 authentication is not configured."
                    : "M365 authentication is not configured. Start via POST /api/authm365/start or GET /api/authm365/start."));
    }

    private M365AuthStartResponse BuildStartResponse(M365TokenProviderOptions options)
    {
        string tenantId = options.TenantId ?? "common";
        string redirectUri = ResolveRedirectUri(options);
        string state = CreateRandomToken(32);
        string codeVerifier = CreateRandomToken(48);
        string codeChallenge = CreateCodeChallenge(codeVerifier);

        string cacheKey = BuildPkceStateCacheKey(state);
        pkceStateCache.Set(
            cacheKey,
            new PkceAuthState(codeVerifier, redirectUri),
            TimeSpan.FromSeconds(PkceStateTtlSeconds));

        string authorizeEndpoint = AuthorizeEndpoint.Replace("{tenantId}", tenantId);
        Dictionary<string, string?> query = new()
        {
            ["client_id"] = options.ClientId,
            ["response_type"] = "code",
            ["redirect_uri"] = redirectUri,
            ["response_mode"] = "query",
            ["scope"] = M365Scope,
            ["state"] = state,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
            ["prompt"] = "select_account"
        };

        string authorizationUrl = QueryHelpers.AddQueryString(authorizeEndpoint, query);
        return new M365AuthStartResponse(authorizationUrl, PkceStateTtlSeconds);
    }

    private string ResolveRedirectUri(M365TokenProviderOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.PublicBaseUrl))
        {
            return $"{options.PublicBaseUrl.TrimEnd('/')}/api/authm365/callback";
        }

        string requestBase = $"{Request.Scheme}://{Request.Host}";
        return $"{requestBase}/api/authm365/callback";
    }

    private static string BuildPkceStateCacheKey(string state) => $"{PkceStateCachePrefix}{state}";

    private static string CreateRandomToken(int byteLength)
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(byteLength);
        return WebEncoders.Base64UrlEncode(bytes);
    }

    private static string CreateCodeChallenge(string codeVerifier)
    {
        byte[] verifierBytes = Encoding.UTF8.GetBytes(codeVerifier);
        byte[] hash = SHA256.HashData(verifierBytes);
        return WebEncoders.Base64UrlEncode(hash);
    }

    private static bool TryCreateM365ConfigurationProblem(
        M365TokenProviderOptions options,
        out ProblemDetails? problem)
    {
        List<string> issues = [];

        if (!options.Enabled)
        {
            issues.Add("MessageScreener__M365Auth__Enabled is false.");
        }

        if (string.IsNullOrWhiteSpace(options.ClientId))
        {
            issues.Add("MessageScreener__M365Auth__ClientId is missing.");
        }

        if (string.IsNullOrWhiteSpace(options.ClientSecret))
        {
            issues.Add("MessageScreener__M365Auth__ClientSecret is missing.");
        }

        if (string.IsNullOrWhiteSpace(options.TenantId))
        {
            issues.Add("MessageScreener__M365Auth__TenantId is missing.");
        }

        if (issues.Count == 0)
        {
            problem = null;
            return false;
        }

        problem = new ProblemDetails
        {
            Title = "M365 Authentication Misconfigured",
            Detail = $"M365 auth cannot start because runtime configuration is incomplete. {string.Join(" ", issues)}",
            Status = StatusCodes.Status400BadRequest
        };

        return true;
    }

    private sealed record PkceAuthState(string CodeVerifier, string RedirectUri);
}

/// <summary>
/// Response from POST /api/authm365/start
/// </summary>
public sealed record M365AuthStartResponse(
    string AuthorizationUrl,
    int ExpiresInSeconds);

/// <summary>
/// Response from GET /api/authm365/status
/// </summary>
public sealed record M365AuthStatusResponse(
    bool IsConfigured,
    string Message);

/// <summary>
/// Logging helpers for AuthM365Controller.
/// </summary>
internal static partial class AuthM365ControllerLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Created authorization URL for PKCE flow.")]
    internal static partial void AuthStartCreated(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Redirecting user to Entra authorization URL.")]
    internal static partial void AuthStartRedirect(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Exception during auth start: {Message}")]
    internal static partial void StartException(ILogger logger, string message);

    [LoggerMessage(Level = LogLevel.Warning, Message = "OAuth callback returned error: {Error}. {Description}")]
    internal static partial void CallbackError(ILogger logger, string error, string description);

    [LoggerMessage(Level = LogLevel.Error, Message = "Token exchange failed. Status: {StatusCode}. Body: {ErrorBody}")]
    internal static partial void TokenExchangeFailed(ILogger logger, System.Net.HttpStatusCode statusCode, string errorBody);

    [LoggerMessage(Level = LogLevel.Information, Message = "M365 tokens obtained and stored.")]
    internal static partial void TokensObtained(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Exception during callback: {Message}")]
    internal static partial void CallbackException(ILogger logger, string message);

    [LoggerMessage(Level = LogLevel.Error, Message = "Exception during revocation: {Message}")]
    internal static partial void RevokeException(ILogger logger, string message);
}
