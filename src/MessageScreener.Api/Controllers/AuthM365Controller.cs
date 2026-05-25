using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.Json.Serialization;
using MessageScreener.Orchestration;

namespace MessageScreener.Api.Controllers;

/// <summary>
/// Handles M365 OAuth2 authentication flow for WorkIQ MCP integration.
/// Owner authenticates once, and refresh token is securely stored.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public sealed class AuthM365Controller(
    IM365TokenProvider m365TokenProvider,
    IHttpClientFactory httpClientFactory,
    ILogger<AuthM365Controller> logger) : ControllerBase
{
    private const string DeviceAuthEndpoint = "https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/devicecode";
    private const string TokenEndpoint = "https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token";
    private const string M365Scope = "Mail.Read Chat.Read TeamsActivity.Read";

    /// <summary>
    /// Initiates M365 Device Flow authentication.
    /// Returns device code and user code that owner uses to authenticate.
    /// </summary>
    [HttpPost("initiate")]
    [ProducesResponseType(typeof(M365AuthInitiateResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> InitiateAsync(
        [FromServices] IOptions<M365TokenProviderOptions> options,
        CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "M365 Authentication Disabled",
                Detail = "M365 authentication is not configured on this instance."
            });
        }

        try
        {
            string tenantId = options.Value.TenantId ?? "common";
            using HttpClient http = httpClientFactory.CreateClient();

            var request = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = options.Value.ClientId!,
                ["scope"] = M365Scope,
            });

            string deviceAuthEndpoint = DeviceAuthEndpoint.Replace("{tenantId}", tenantId);
            using HttpResponseMessage response = await http.PostAsync(deviceAuthEndpoint, request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                string errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                AuthM365ControllerLog.InitiateFailed(logger, response.StatusCode, errorBody);
                return StatusCode((int)response.StatusCode, new ProblemDetails
                {
                    Title = "Device Code Request Failed",
                    Detail = $"Failed to initiate device flow: {response.StatusCode}"
                });
            }

            string content = await response.Content.ReadAsStringAsync(cancellationToken);
            JsonElement json = JsonSerializer.Deserialize<JsonElement>(content);

            string deviceCode = json.GetProperty("device_code").GetString()
                ?? throw new InvalidOperationException("Missing device_code in response.");
            string userCode = json.GetProperty("user_code").GetString()
                ?? throw new InvalidOperationException("Missing user_code in response.");
            string verificationUri = json.GetProperty("verification_uri").GetString()
                ?? throw new InvalidOperationException("Missing verification_uri in response.");

            AuthM365ControllerLog.DeviceCodeIssued(logger, userCode);

            return Ok(new M365AuthInitiateResponse(
                DeviceCode: deviceCode,
                UserCode: userCode,
                VerificationUri: verificationUri,
                ExpiresIn: json.GetProperty("expires_in").GetInt32(),
                Interval: json.TryGetProperty("interval", out JsonElement intVal)
                    ? intVal.GetInt32()
                    : 5));
        }
        catch (Exception ex)
        {
            AuthM365ControllerLog.InitiateException(logger, ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "Initiation Failed",
                Detail = $"An error occurred: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Polls for device flow authentication completion.
    /// Owner visits VerificationUri with UserCode; once they complete auth, this returns the token.
    /// </summary>
    [HttpPost("poll")]
    [ProducesResponseType(typeof(M365AuthPollResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(M365AuthPollResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> PollAsync(
        [FromBody] M365AuthPollRequest request,
        [FromServices] IOptions<M365TokenProviderOptions> options,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.DeviceCode))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid Request",
                Detail = "DeviceCode is required."
            });
        }

        try
        {
            string tenantId = options.Value.TenantId ?? "common";
            using HttpClient http = httpClientFactory.CreateClient();

            var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = options.Value.ClientId!,
                ["client_secret"] = options.Value.ClientSecret!,
                ["device_code"] = request.DeviceCode,
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
            });

            string tokenEndpoint = TokenEndpoint.Replace("{tenantId}", tenantId);
            using HttpResponseMessage response = await http.PostAsync(tokenEndpoint, tokenRequest, cancellationToken);

            string content = await response.Content.ReadAsStringAsync(cancellationToken);
            JsonElement json = JsonSerializer.Deserialize<JsonElement>(content);

            // Device flow returns "authorization_pending" while waiting.
            if (json.TryGetProperty("error", out JsonElement errorProp))
            {
                string error = errorProp.GetString() ?? "unknown";

                if (error == "authorization_pending")
                {
                    AuthM365ControllerLog.AuthorizationPending(logger);
                    return Accepted(new M365AuthPollResponse(
                        Status: "pending",
                        Message: "Waiting for owner authorization.",
                        IsAuthorized: false));
                }

                if (error == "expired_token")
                {
                    AuthM365ControllerLog.DeviceCodeExpired(logger);
                    return BadRequest(new ProblemDetails
                    {
                        Title = "Device Code Expired",
                        Detail = "The device code has expired. Please initiate again."
                    });
                }

                AuthM365ControllerLog.TokenExchangeFailed(logger, error);
                return BadRequest(new ProblemDetails
                {
                    Title = "Authentication Failed",
                    Detail = $"Error: {error}"
                });
            }

            // Success: extract tokens.
            string accessToken = json.GetProperty("access_token").GetString()
                ?? throw new InvalidOperationException("Missing access_token in response.");
            string refreshToken = json.GetProperty("refresh_token").GetString()
                ?? throw new InvalidOperationException("Missing refresh_token in response.");

            // Store refresh token securely.
            await m365TokenProvider.StoreM365RefreshTokenAsync(refreshToken, cancellationToken);

            AuthM365ControllerLog.TokensObtained(logger);

            return Ok(new M365AuthPollResponse(
                Status: "authorized",
                Message: "Authorization successful. WorkIQ can now access your M365 data.",
                IsAuthorized: true,
                AccessToken: accessToken));
        }
        catch (Exception ex)
        {
            AuthM365ControllerLog.PollException(logger, ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "Poll Failed",
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
    public async Task<IActionResult> StatusAsync(CancellationToken cancellationToken)
    {
        bool hasAuth = await m365TokenProvider.HasValidM365AuthAsync(cancellationToken);

        return Ok(new M365AuthStatusResponse(
            IsConfigured: hasAuth,
            Message: hasAuth
                ? "M365 authentication is configured. WorkIQ can access your data."
                : "M365 authentication is not configured. Please authenticate via POST /api/auth-m365/initiate."));
    }
}

/// <summary>
/// Response from POST /api/auth-m365/initiate
/// </summary>
public sealed record M365AuthInitiateResponse(
    string DeviceCode,
    string UserCode,
    string VerificationUri,
    int ExpiresIn,
    int Interval);

/// <summary>
/// Request to POST /api/auth-m365/poll
/// </summary>
public sealed record M365AuthPollRequest(
    [property: JsonPropertyName("device_code")]
    string DeviceCode);

/// <summary>
/// Response from POST /api/auth-m365/poll
/// </summary>
public sealed record M365AuthPollResponse(
    string Status,
    string Message,
    bool IsAuthorized,
    string? AccessToken = null);

/// <summary>
/// Response from GET /api/auth-m365/status
/// </summary>
public sealed record M365AuthStatusResponse(
    bool IsConfigured,
    string Message);

/// <summary>
/// Logging helpers for AuthM365Controller.
/// </summary>
internal static partial class AuthM365ControllerLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Device code issued for user code: {UserCode}")]
    internal static partial void DeviceCodeIssued(ILogger logger, string userCode);

    [LoggerMessage(Level = LogLevel.Error, Message = "Device code initiation failed. Status: {StatusCode}. Body: {ErrorBody}")]
    internal static partial void InitiateFailed(ILogger logger, System.Net.HttpStatusCode statusCode, string errorBody);

    [LoggerMessage(Level = LogLevel.Error, Message = "Exception during initiation: {Message}")]
    internal static partial void InitiateException(ILogger logger, string message);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Authorization pending. Owner has not yet authenticated.")]
    internal static partial void AuthorizationPending(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Device code has expired.")]
    internal static partial void DeviceCodeExpired(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Token exchange failed with error: {Error}")]
    internal static partial void TokenExchangeFailed(ILogger logger, string error);

    [LoggerMessage(Level = LogLevel.Information, Message = "M365 tokens obtained and stored.")]
    internal static partial void TokensObtained(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Exception during poll: {Message}")]
    internal static partial void PollException(ILogger logger, string message);

    [LoggerMessage(Level = LogLevel.Information, Message = "M365 authentication revoked.")]
    internal static partial void Revoked(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Exception during revocation: {Message}")]
    internal static partial void RevokeException(ILogger logger, string message);
}
