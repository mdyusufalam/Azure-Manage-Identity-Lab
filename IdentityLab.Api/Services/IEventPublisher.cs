namespace IdentityLab.Api.Services;

/// <summary>
/// Publishes domain events to Azure Event Grid. Authentication is via the shared
/// <see cref="Azure.Identity.DefaultAzureCredential"/> behind the injected
/// <see cref="Azure.Messaging.EventGrid.EventGridPublisherClient"/> - no topic key.
/// </summary>
public interface IEventPublisher
{
    /// <summary>Publishes a single "FileUploaded" event carrying the blob name and size.</summary>
    Task PublishFileUploadedAsync(string blobName, long sizeInBytes, CancellationToken cancellationToken);
}
