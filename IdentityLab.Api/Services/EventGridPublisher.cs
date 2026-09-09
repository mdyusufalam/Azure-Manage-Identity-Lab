using Azure.Messaging.EventGrid;

namespace IdentityLab.Api.Services;

/// <inheritdoc />
public sealed class EventGridPublisher : IEventPublisher
{
    private readonly EventGridPublisherClient _client;
    private readonly ILogger<EventGridPublisher> _logger;

    public EventGridPublisher(EventGridPublisherClient client, ILogger<EventGridPublisher> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task PublishFileUploadedAsync(string blobName, long sizeInBytes, CancellationToken cancellationToken)
    {
        // Event Grid custom-topic schema. "data" is an arbitrary payload the
        // subscriber gets to inspect; subject/eventType are what subscribers
        // usually filter on.
        var eventGridEvent = new EventGridEvent(
            subject: $"files/{blobName}",
            eventType: "FileUploaded",
            dataVersion: "1.0",
            data: new
            {
                blobName,
                sizeInBytes,
                uploadedAtUtc = DateTime.UtcNow,
            });

        _logger.LogInformation("Publishing FileUploaded event for {BlobName}.", blobName);
        await _client.SendEventAsync(eventGridEvent, cancellationToken);
    }
}
