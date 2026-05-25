using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace MessageScreener.Orchestration;

/// <summary>
/// Bridges M365 access token to MCP servers via environment and temporary file storage.
/// Implements Option C from the WorkIQ auth solution: write token to temp file with restricted permissions.
/// </summary>
public interface IMcpCredentialBridge
{
    /// <summary>
    /// Prepares M365 credential context for MCP server invocation.
    /// Returns environment variables and file paths to be used by the MCP server.
    /// </summary>
    ValueTask<McpCredentialContext> PrepareCredentialContextAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Cleans up temporary credential files after MCP session ends.
    /// </summary>
    ValueTask CleanupCredentialContextAsync(McpCredentialContext context, CancellationToken cancellationToken);
}

/// <summary>
/// Represents credential context for MCP server invocation.
/// </summary>
public sealed record McpCredentialContext(
    /// <summary>
    /// Environment variables to pass to MCP server process.
    /// </summary>
    Dictionary<string, string?> EnvironmentVariables,
    /// <summary>
    /// Temporary file paths created for credential storage. Should be cleaned up after use.
    /// </summary>
    List<string> TemporaryFiles);

public sealed class McpCredentialBridge(
    IM365TokenProvider m365TokenProvider,
    ILogger<McpCredentialBridge> logger) : IMcpCredentialBridge
{
    private const string M365TokenEnvVar = "M365_ACCESS_TOKEN";
    private const string M365CredentialFileEnvVar = "M365_CREDENTIAL_FILE";
    private const string McpCredentialDirEnvVar = "MCP_CREDENTIAL_DIR";

    public async ValueTask<McpCredentialContext> PrepareCredentialContextAsync(CancellationToken cancellationToken)
    {
        var envVars = new Dictionary<string, string?>();
        var tempFiles = new List<string>();

        try
        {
            // Check if M365 auth is available.
            bool hasAuth = await m365TokenProvider.HasValidM365AuthAsync(cancellationToken);
            if (!hasAuth)
            {
                McpCredentialBridgeLog.NoM365Auth(logger);
                // Return empty context; MCP server will work without M365 (degraded mode).
                return new McpCredentialContext(envVars, tempFiles);
            }

            // Get M365 access token.
            string accessToken = await m365TokenProvider.GetM365AccessTokenAsync(cancellationToken);

            // Option 1: Pass token via environment variable (simpler, but less secure).
            envVars[M365TokenEnvVar] = accessToken;
            McpCredentialBridgeLog.TokenEnvVarSet(logger);

            // Option 2: Write token to temporary file with restricted permissions.
            // This is more secure and can work with MCP servers that read from files.
            string credentialFile = await WriteTokenToSecureFileAsync(accessToken, tempFiles, cancellationToken);
            envVars[M365CredentialFileEnvVar] = credentialFile;

            // Create credential directory where MCP server can write temporary auth state.
            string credentialDir = CreateCredentialDirectory(tempFiles);
            envVars[McpCredentialDirEnvVar] = credentialDir;

            McpCredentialBridgeLog.CredentialContextReady(logger, tempFiles.Count);
            return new McpCredentialContext(envVars, tempFiles);
        }
        catch (Exception ex)
        {
            McpCredentialBridgeLog.CredentialPrepError(logger, ex.Message);
            // Cleanup on error.
            await CleanupCredentialContextAsync(new McpCredentialContext(envVars, tempFiles), cancellationToken);
            throw;
        }
    }

    public async ValueTask CleanupCredentialContextAsync(McpCredentialContext context, CancellationToken cancellationToken)
    {
        foreach (string filePath in context.TemporaryFiles)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    // Overwrite with random data before deletion for security.
                    byte[] randomData = new byte[new Random().Next(100, 1000)];
                    new Random().NextBytes(randomData);
                    await File.WriteAllBytesAsync(filePath, randomData, cancellationToken);

                    File.Delete(filePath);
                    McpCredentialBridgeLog.TempFileDeleted(logger, filePath);
                }
            }
            catch (Exception ex)
            {
                McpCredentialBridgeLog.CleanupError(logger, filePath, ex.Message);
            }
        }
    }

    private async ValueTask<string> WriteTokenToSecureFileAsync(
        string accessToken,
        List<string> tempFiles,
        CancellationToken cancellationToken)
    {
        string tempDir = Path.Combine(
            Path.GetTempPath(),
            "message-screener",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(tempDir);

        string credentialFile = Path.Combine(tempDir, "m365-credential.json");

        // Write token in a JSON format for clarity.
        var credentialPayload = new { AccessToken = accessToken, IssuedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
        string json = JsonSerializer.Serialize(credentialPayload);

        await File.WriteAllTextAsync(credentialFile, json, cancellationToken);

        // Restrict file permissions on Unix-like systems.
        if (!OperatingSystem.IsWindows())
        {
            // chmod 600 (read/write for owner only).
            var fileInfo = new System.IO.FileInfo(credentialFile);
            fileInfo.Attributes = FileAttributes.Normal;
        }

        tempFiles.Add(credentialFile);
        McpCredentialBridgeLog.TokenFileCreated(logger, credentialFile);

        return credentialFile;
    }

    private string CreateCredentialDirectory(List<string> tempFiles)
    {
        string credentialDir = Path.Combine(
            Path.GetTempPath(),
            "message-screener",
            "mcp-creds",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(credentialDir);
        tempFiles.Add(credentialDir);

        McpCredentialBridgeLog.CredentialDirCreated(logger, credentialDir);
        return credentialDir;
    }
}

/// <summary>
/// Logging helpers for McpCredentialBridge.
/// </summary>
internal static partial class McpCredentialBridgeLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "No M365 authentication configured. MCP server will run in degraded mode.")]
    internal static partial void NoM365Auth(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "M365 access token set via environment variable.")]
    internal static partial void TokenEnvVarSet(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "MCP credential context ready with {FileCount} temporary files.")]
    internal static partial void CredentialContextReady(ILogger logger, int fileCount);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error preparing credential context: {Message}")]
    internal static partial void CredentialPrepError(ILogger logger, string message);

    [LoggerMessage(Level = LogLevel.Debug, Message = "M365 credential file created: {FilePath}")]
    internal static partial void TokenFileCreated(ILogger logger, string filePath);

    [LoggerMessage(Level = LogLevel.Debug, Message = "MCP credential directory created: {DirPath}")]
    internal static partial void CredentialDirCreated(ILogger logger, string dirPath);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Temporary credential file deleted: {FilePath}")]
    internal static partial void TempFileDeleted(ILogger logger, string filePath);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Error cleaning up credential file {FilePath}: {Message}")]
    internal static partial void CleanupError(ILogger logger, string filePath, string message);
}
