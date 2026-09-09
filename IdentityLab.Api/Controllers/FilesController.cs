using Azure;
using IdentityLab.Api.Models;
using IdentityLab.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace IdentityLab.Api.Controllers;

[ApiController]
[Route("api/files")]
public sealed class FilesController : ControllerBase
{
    // 15-minute download links, per the spec.
    private static readonly TimeSpan DownloadUrlLifetime = TimeSpan.FromMinutes(15);

    private readonly IBlobStorageService _blobStorage;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<FilesController> _logger;

    public FilesController(
        IBlobStorageService blobStorage,
        IEventPublisher eventPublisher,
        ILogger<FilesController> logger)
    {
        _blobStorage = blobStorage;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    /// <summary>
    /// Uploads a file to Blob Storage, then publishes a "FileUploaded" event to Event Grid.
    /// </summary>
    [HttpPost("upload")]
    [RequestSizeLimit(50 * 1024 * 1024)] // 50 MB is plenty for a demo
    [ProducesResponseType(typeof(UploadResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Upload(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest("Provide a non-empty file in a multipart/form-data field named 'file'.");
        }

        BlobUploadResult uploadResult;
        try
        {
            await using var stream = file.OpenReadStream();
            uploadResult = await _blobStorage.UploadAsync(
                file.FileName, stream, file.ContentType, cancellationToken);
        }
        catch (RequestFailedException ex)
        {
            return HandleStorageFailure(ex, "uploading a blob",
                missingRoleHint: "Storage Blob Data Contributor on the storage account");
        }

        // The upload already succeeded. If the notification fails, we log loudly
        // (so a missing Event Grid role is obvious) but still return 200 - the
        // file is safely stored and the caller should not be told otherwise.
        try
        {
            await _eventPublisher.PublishFileUploadedAsync(
                uploadResult.BlobName, uploadResult.SizeInBytes, cancellationToken);
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex,
                "Event Grid publish failed after a successful upload (status {Status}). " +
                "Check the 'EventGrid Data Sender' role on the topic.", ex.Status);
        }

        return Ok(new UploadResponse(uploadResult.BlobName, uploadResult.SizeInBytes));
    }

    /// <summary>
    /// Returns a read-only user delegation SAS URL for the blob, valid for 15 minutes.
    /// </summary>
    [HttpGet("{blobName}/download-url")]
    [ProducesResponseType(typeof(DownloadUrlResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetDownloadUrl(string blobName, CancellationToken cancellationToken)
    {
        try
        {
            var url = await _blobStorage.CreateDownloadUrlAsync(
                blobName, DownloadUrlLifetime, cancellationToken);

            if (url is null)
            {
                return NotFound($"Blob '{blobName}' was not found.");
            }

            return Ok(new DownloadUrlResponse(
                blobName, url.ToString(), (int)DownloadUrlLifetime.TotalMinutes));
        }
        catch (RequestFailedException ex)
        {
            return HandleStorageFailure(ex, "creating a user delegation SAS",
                missingRoleHint: "Storage Blob Data Delegator on the storage account " +
                                 "(required to request a user delegation key)");
        }
    }

    /// <summary>
    /// Turns an Azure <see cref="RequestFailedException"/> into a sensible HTTP response
    /// and logs which service and role are the likely culprit.
    /// </summary>
    private IActionResult HandleStorageFailure(RequestFailedException ex, string operation, string missingRoleHint)
    {
        if (ex.Status == StatusCodes.Status403Forbidden)
        {
            _logger.LogError(ex,
                "Azure Blob Storage denied '{Operation}' with 403. Likely missing role: {RoleHint}.",
                operation, missingRoleHint);

            return Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Azure Blob Storage access denied",
                detail: $"The identity is missing '{missingRoleHint}'.");
        }

        _logger.LogError(ex,
            "Azure Blob Storage request failed while {Operation} (status {Status}).", operation, ex.Status);

        // ex.Status is 0 for network-level failures; map that to 502.
        var status = ex.Status == 0 ? StatusCodes.Status502BadGateway : ex.Status;
        return Problem(statusCode: status, title: "Azure Blob Storage request failed", detail: ex.Message);
    }
}
