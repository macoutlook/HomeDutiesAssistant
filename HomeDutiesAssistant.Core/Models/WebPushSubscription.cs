namespace HomeDutiesAssistant.Models;

public sealed record WebPushSubscription(string UserId, long HomeId, string Endpoint, string EncryptionKey, string Auth);