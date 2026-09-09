namespace IdentityLab.Api.Models;

/// <summary>Result of storing a blob: the generated name and its size in bytes.</summary>
public sealed record BlobUploadResult(string BlobName, long SizeInBytes);

/// <summary>Response body for POST /api/files/upload.</summary>
public sealed record UploadResponse(string BlobName, long SizeInBytes);

/// <summary>Response body for GET /api/files/{blobName}/download-url.</summary>
public sealed record DownloadUrlResponse(string BlobName, string Url, int ExpiresInMinutes);
