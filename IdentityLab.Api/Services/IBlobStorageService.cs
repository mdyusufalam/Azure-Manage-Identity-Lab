using IdentityLab.Api.Models;

namespace IdentityLab.Api.Services;

/// <summary>
/// Wraps Azure Blob Storage access. All authentication is via the shared
/// <see cref="Azure.Identity.DefaultAzureCredential"/> behind the injected
/// <see cref="Azure.Storage.Blobs.BlobServiceClient"/> - no account keys.
/// </summary>
public interface IBlobStorageService
{
    /// <summary>Uploads <paramref name="content"/> to the configured container under a unique name.</summary>
    Task<BlobUploadResult> UploadAsync(
        string originalFileName,
        Stream content,
        string? contentType,
        CancellationToken cancellationToken);

    /// <summary>
    /// Builds a read-only, time-limited download URL for an existing blob using a
    /// <b>user delegation SAS</b> (signed with an Entra ID key, not an account key).
    /// Returns <c>null</c> if the blob does not exist.
    /// </summary>
    Task<Uri?> CreateDownloadUrlAsync(
        string blobName,
        TimeSpan validFor,
        CancellationToken cancellationToken);
}
