using System.Text.Json;
using Azure;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MessageScreener.ReviewDelivery;

public interface IPersonalReviewConversationRegistry
{
    ValueTask<PersonalReviewConversationContext?> GetCurrentAsync(CancellationToken cancellationToken);

    ValueTask RememberAsync(PersonalReviewConversationContext context, CancellationToken cancellationToken);
}

public sealed record PersonalReviewConversationContext(string ConversationId, string ServiceUrl);

public sealed class KeyVaultPersonalReviewConversationRegistry(
    IOptions<MessageScreenerTeamsOptions> options,
    ILogger<KeyVaultPersonalReviewConversationRegistry> logger) : IPersonalReviewConversationRegistry
{
    private const string SecretName = "message-screener-personal-review-conversation";
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SecretClient? _secretClient = CreateSecretClient(options.Value);
    private PersonalReviewConversationContext? _current;
    private bool _loaded;

    public async ValueTask<PersonalReviewConversationContext?> GetCurrentAsync(CancellationToken cancellationToken)
    {
        PersonalReviewConversationContext? current = Volatile.Read(ref _current);
        if (current is not null)
        {
            return current;
        }

        if (_loaded)
        {
            return null;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            current = _current;
            if (current is not null || _loaded)
            {
                return current;
            }

            current = await LoadAsync(cancellationToken);
            _current = current;
            _loaded = true;
            return current;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask RememberAsync(PersonalReviewConversationContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.ConversationId) || string.IsNullOrWhiteSpace(context.ServiceUrl))
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            _current = context;
            _loaded = true;
        }
        finally
        {
            _gate.Release();
        }

        if (_secretClient is null)
        {
            return;
        }

        try
        {
            string payload = JsonSerializer.Serialize(context);
            await _secretClient.SetSecretAsync(SecretName, payload, cancellationToken);
            ReviewDeliveryLog.PersonalReviewConversationPersisted(logger, context.ConversationId);
        }
        catch (Exception ex)
        {
            ReviewDeliveryLog.PersonalReviewConversationPersistFailed(logger, context.ConversationId, ex.Message);
        }
    }

    private async ValueTask<PersonalReviewConversationContext?> LoadAsync(CancellationToken cancellationToken)
    {
        if (_secretClient is null)
        {
            return null;
        }

        try
        {
            Response<KeyVaultSecret> response = await _secretClient.GetSecretAsync(SecretName, cancellationToken: cancellationToken);
            PersonalReviewConversationContext? context = JsonSerializer.Deserialize<PersonalReviewConversationContext>(response.Value.Value);
            if (context is null || string.IsNullOrWhiteSpace(context.ConversationId) || string.IsNullOrWhiteSpace(context.ServiceUrl))
            {
                return null;
            }

            ReviewDeliveryLog.PersonalReviewConversationLoaded(logger, context.ConversationId);
            return context;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
        catch (Exception ex)
        {
            ReviewDeliveryLog.PersonalReviewConversationLoadFailed(logger, ex.Message);
            return null;
        }
    }

    private static SecretClient? CreateSecretClient(MessageScreenerTeamsOptions teamsOptions)
    {
        if (string.IsNullOrWhiteSpace(teamsOptions.KeyVaultUri))
        {
            return null;
        }

        var credentialOptions = new DefaultAzureCredentialOptions();
        if (!string.IsNullOrWhiteSpace(teamsOptions.ManagedIdentityClientId))
        {
            credentialOptions.ManagedIdentityClientId = teamsOptions.ManagedIdentityClientId;
        }

        return new SecretClient(new Uri(teamsOptions.KeyVaultUri), new DefaultAzureCredential(credentialOptions));
    }
}
