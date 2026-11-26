using System.Linq.Expressions;
using System.Text.Json;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Googols.Application.Abstractions;
using Hangfire;
using Microsoft.Extensions.Caching.Distributed;

namespace Googols.Infrastructure.Services;

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;

    public DateOnly TodayIn(string timeZoneId)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId); // "Asia/Kolkata" works on Windows (.NET 6+ ICU) and Linux
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz));
    }
}

/// <summary>Azure Blob Storage. Blob names are server-generated GUIDs; the user's file name is never used as a path.</summary>
public sealed class BlobFileStorage(BlobServiceClient blobs) : IFileStorage
{
    public async Task<string> SaveAsync(string container, string fileName, Stream content, string contentType, CancellationToken ct)
    {
        var c = blobs.GetBlobContainerClient(container);
        await c.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        var blobName = $"{DateTime.UtcNow:yyyy/MM}/{Guid.NewGuid():N}{ext}";
        await c.GetBlobClient(blobName).UploadAsync(content, new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } }, ct);
        return blobName;
    }

    public async Task<Stream?> OpenReadAsync(string container, string blobName, CancellationToken ct)
    {
        var blob = blobs.GetBlobContainerClient(container).GetBlobClient(blobName);
        return await blob.ExistsAsync(ct) ? await blob.OpenReadAsync(cancellationToken: ct) : null;
    }

    public Task DeleteAsync(string container, string blobName, CancellationToken ct) =>
        blobs.GetBlobContainerClient(container).GetBlobClient(blobName).DeleteIfExistsAsync(cancellationToken: ct);
}

public sealed class HangfireBackgroundJobs(IBackgroundJobClient client) : IBackgroundJobs
{
    public string Enqueue<TJob>(Expression<Func<TJob, Task>> job) => client.Enqueue(job);
}

/// <summary>Redis via IDistributedCache (falls back to in-memory when no Redis connection string is set).</summary>
public sealed class DistributedCacheAdapter(IDistributedCache cache) : ICache
{
    public async Task<T?> GetAsync<T>(string key, CancellationToken ct)
    {
        var bytes = await cache.GetAsync(key, ct);
        return bytes is null ? default : JsonSerializer.Deserialize<T>(bytes);
    }

    public Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct) =>
        cache.SetAsync(key, JsonSerializer.SerializeToUtf8Bytes(value), new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl }, ct);

    public Task RemoveAsync(string key, CancellationToken ct) => cache.RemoveAsync(key, ct);
}
