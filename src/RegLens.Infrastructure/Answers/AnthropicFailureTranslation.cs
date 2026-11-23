using System.Net;
using Anthropic.Exceptions;
using Microsoft.Extensions.AI;
using RegLens.Application.Answers;

namespace RegLens.Infrastructure.Answers;

/// <summary>
/// Turns the Anthropic SDK's transient failures into <see cref="AnswerFailure.ModelUnavailable"/> (spec 0003, P12),
/// so Application never sees SDK types. Runs after the SDK's own retries. A 400, 401, 403 or 404 is a bug or a
/// configuration error, not an outage, and is left to surface as a 500.
/// </summary>
public sealed class AnthropicFailureTranslation(IChatClient inner) : DelegatingChatClient(inner)
{
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsTransient(exception))
        {
            throw new AnswerGenerationException(AnswerFailure.ModelUnavailable, "The answer model is unavailable.", exception);
        }
    }

    private static bool IsTransient(Exception exception) => exception switch
    {
        AnthropicApiException api => api.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout
            || (int)api.StatusCode >= 500,
        AnthropicIOException or HttpRequestException or TimeoutException => true,
        // The SDK's per-request timeout cancels without the caller asking (filtered above).
        OperationCanceledException => true,
        _ => false,
    };
}
