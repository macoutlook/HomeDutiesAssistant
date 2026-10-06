using System.Net;
using System.Text.Json;
using HomeDutiesAssistant.Configuration;
using HomeDutiesAssistant.Infrastructure;
using HomeDutiesAssistant.Models;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using Microsoft.Extensions.Options;

namespace HomeDutiesAssistant.Web.Services;

// Encrypts, VAPID-signs and delivers one push message. Registered only when
// both VAPID keys are configured (see Program.cs), so resolve it with
// GetService<PushSender>(); null => push disabled.
public sealed class PushSender(
    IHttpClientFactory httpClientFactory,
    IOptions<PushOptions> options,
    PushRepository repository,
    ILogger<PushSender> logger)
{
    public const string HttpClientName = "WebPush";

    // Undelivered messages are dropped after a day; a reminder is stale by then.
    private const int TimeToLiveSeconds = 24 * 60 * 60;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly VapidAuthentication _vapid = new(options.Value.Vapid.PublicKey, options.Value.Vapid.PrivateKey)
    {
        Subject = options.Value.Vapid.Subject,
    };

    public string PublicKey => options.Value.Vapid.PublicKey;

    // True when the push service accepted the message. A 404/410 means the
    // browser dropped the subscription, so it is deleted.
    public async Task<bool> SendAsync(WebPushSubscription subscription, PushPayload payload, CancellationToken ct = default)
    {
        var client = new PushServiceClient(httpClientFactory.CreateClient(HttpClientName))
        {
            DefaultAuthentication = _vapid,
        };
        var target = new PushSubscription { Endpoint = subscription.Endpoint };
        target.SetKey(PushEncryptionKeyName.P256DH, subscription.EncryptionKey);
        target.SetKey(PushEncryptionKeyName.Auth, subscription.Auth);
        var message = new PushMessage(JsonSerializer.Serialize(payload, JsonOptions)) { TimeToLive = TimeToLiveSeconds };

        try
        {
            await client.RequestPushMessageDeliveryAsync(target, message, ct);
            return true;
        }
        catch (PushServiceClientException exception)
            when (exception.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            logger.LogInformation("Push subscription for user {UserId} expired; removing it.", subscription.UserId);
            try
            {
                await repository.DeleteByEndpointAsync(subscription.Endpoint, ct);
            }
            catch (Exception deleteException) when (deleteException is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogWarning(deleteException, "Could not remove expired push subscription for user {UserId}.",
                    subscription.UserId);
            }
            return false;
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Push to user {UserId} failed.", subscription.UserId);
            return false;
        }
    }
}

public sealed record PushPayload(string Title, string Body, string Url, string Tag);
