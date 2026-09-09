using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using IdentityLab.Api.Models;

namespace IdentityLab.Api.Services;

/// <inheritdoc />
public sealed class BlobStorageService : IBlobStorageService
{
    private readonly BlobServiceClient _serviceClient;
    private readonly ILogger<BlobStorageService> _logger;
    private readonly string _containerName;

    public BlobStorageService(
        BlobServiceClient serviceClient,
        IConfiguration config,
        ILogger<BlobStorageService> logger)
    {
        _serviceClient = serviceClient;
        _logger = logger;
        _containerName = config["Azure:Storage:ContainerName"] ?? "uploads";
    }

    public async Task<BlobUploadResult> UploadAsync(
        string originalFileName,
        Stream content,
        string? contentType,
        CancellationToken cancellationToken)
    {
        var container = _serviceClient.GetBlobContainerClient(_containerName);
        await container.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

        var blobName = BuildUniqueBlobName(originalFileName);
        var blob = container.GetBlobClient(blobName);

        var options = new BlobUploadOptions();
        if (!string.IsNullOrWhiteSpace(contentType))
        {
            options.HttpHeaders = new BlobHttpHeaders { ContentType = contentType };
        }

        _logger.LogInformation("Uploading blob {BlobName} to container {Container}.", blobName, _containerName);
        await blob.UploadAsync(content, options, cancellationToken);

        var properties = await blob.GetPropertiesAsync(cancellationToken: cancellationToken);
        return new BlobUploadResult(blobName, properties.Value.ContentLength);
    }

    public async Task<Uri?> CreateDownloadUrlAsync(
        string blobName,
        TimeSpan validFor,
        CancellationToken cancellationToken)
    {
        var container = _serviceClient.GetBlobContainerClient(_containerName);
        var blob = container.GetBlobClient(blobName);

        if (!await blob.ExistsAsync(cancellationToken))
        {
            _logger.LogWarning("Download URL requested for missing blob {BlobName}.", blobName);
            return null;
        }

        // Small backdated start time absorbs minor clock skew between us and Azure.
        var startsOn = DateTimeOffset.UtcNow.AddMinutes(-5);
        var expiresOn = DateTimeOffset.UtcNow.Add(validFor);

        // The important part: ask Storage for a *user delegation key*. That key is
        // issued and signed by Entra ID on behalf of our managed identity. We then
        // sign the SAS with it - so at no point is a storage account key involved.
        UserDelegationKey delegationKey = await _serviceClient.GetUserDelegationKeyAsync(
            startsOn, expiresOn, cancellationToken);

        var sasBuilder = new BlobSasBuilder
        {
            BlobContainerName = _containerName,
            BlobName = blobName,
            Resource = "b", // "b" = blob
            StartsOn = startsOn,
            ExpiresOn = expiresOn,
            Protocol = SasProtocol.Https,
        };
        sasBuilder.SetPermissions(BlobSasPermissions.Read);

        var sasToken = sasBuilder
            .ToSasQueryParameters(delegationKey, _serviceClient.AccountName)
            .ToString();

        var downloadUri = new UriBuilder(blob.Uri) { Query = sasToken }.Uri;
        _logger.LogInformation(
            "Issued user delegation SAS for {BlobName}, valid until {ExpiresOn:o}.", blobName, expiresOn);
        return downloadUri;
    }

    /// <summary>
    /// Keeps the readable part of the original name but appends a timestamp and a
    /// short GUID so repeated demo uploads of "photo.png" never overwrite each other.
    /// </summary>
    private static string BuildUniqueBlobName(string originalFileName)
    {
        var safeName = Path.GetFileNameWithoutExtension(originalFileName);
        var extension = Path.GetExtension(originalFileName);
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var shortId = Guid.NewGuid().ToString("N")[..8];
        return $"{safeName}-{stamp}-{shortId}{extension}";
    }
}
