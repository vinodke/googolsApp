namespace Googols.Application.Abstractions;

// Ports: the application layer depends only on these interfaces. Infrastructure supplies
// Azure / SQL / Redis / Hangfire implementations; tests supply fakes. Swapping a vendor
// (e.g. OpenAI -> another model provider) never touches learning logic.

public interface IClock
{
    DateTime UtcNow { get; }
    DateOnly TodayIn(string timeZoneId);
}

public interface ICurrentUser
{
    Guid? UserId { get; }
    bool IsInRole(string role);
}

/// <summary>Large files (PDFs, images, audio, video) live in object storage, never in SQL Server.</summary>
public interface IFileStorage
{
    Task<string> SaveAsync(string container, string fileName, Stream content, string contentType, CancellationToken ct);
    Task<Stream?> OpenReadAsync(string container, string blobName, CancellationToken ct);
    Task DeleteAsync(string container, string blobName, CancellationToken ct);
}

/// <summary>Enqueues work for the background worker (Hangfire in production).</summary>
public interface IBackgroundJobs
{
    string Enqueue<TJob>(System.Linq.Expressions.Expression<Func<TJob, Task>> job);
}

/// <summary>Pushes realtime events to connected clients (SignalR in production).</summary>
public interface IRealtimeNotifier
{
    Task NotifyStudentAsync(Guid studentId, string eventName, object payload, CancellationToken ct);
}

public interface ICache
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct);
    Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct);
    Task RemoveAsync(string key, CancellationToken ct);
}
