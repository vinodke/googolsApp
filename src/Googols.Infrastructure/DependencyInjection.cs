using Azure.Storage.Blobs;
using Googols.Application.Abstractions;
using Googols.Application.Ai;
using Googols.Infrastructure.Ai;
using Googols.Infrastructure.Persistence;
using Googols.Infrastructure.Services;
using Hangfire;
using Hangfire.SqlServer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Googols.Infrastructure;

public static class DependencyInjection
{
    /// <param name="runHangfireServer">True only in Googols.Worker; the API enqueues jobs but does not execute them.</param>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config, bool runHangfireServer = false)
    {
        var sql = config.GetConnectionString("Googols")
            ?? throw new InvalidOperationException("ConnectionStrings:Googols is not set. Use dotnet user-secrets or an environment variable.");

        services.AddDbContext<GoogolsDbContext>(o => o.UseSqlServer(sql, s =>
        {
            s.EnableRetryOnFailure(3);
            s.MigrationsHistoryTable("__EFMigrationsHistory", "dbo");
        }));

        // Redis cache, or in-memory when Redis isn't configured (local dev).
        var redis = config.GetConnectionString("Redis");
        if (!string.IsNullOrWhiteSpace(redis))
        {
            services.AddStackExchangeRedisCache(o => { o.Configuration = redis; o.InstanceName = "googols:"; });
        }
        else
        {
            services.AddDistributedMemoryCache();
        }
        services.AddSingleton<ICache, DistributedCacheAdapter>();

        // Azure Blob Storage ("UseDevelopmentStorage=true" works with Azurite locally).
        var blobs = config.GetConnectionString("Blob") ?? "UseDevelopmentStorage=true";
        services.AddSingleton(new BlobServiceClient(blobs));
        services.AddSingleton<IFileStorage, BlobFileStorage>();

        // Hangfire on the same SQL Server database, in its own schema.
        services.AddHangfire(h => h
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UseSqlServerStorage(sql, new SqlServerStorageOptions { SchemaName = "jobs", PrepareSchemaIfNecessary = true }));
        if (runHangfireServer)
        {
            services.AddHangfireServer();
        }
        services.AddScoped<IBackgroundJobs, HangfireBackgroundJobs>();

        // AI
        services.Configure<AzureOpenAiOptions>(config.GetSection(AzureOpenAiOptions.Section));
        services.Configure<AzureSearchOptions>(config.GetSection(AzureSearchOptions.Section));
        services.AddSingleton<IAiModelProvider, AzureOpenAiProvider>();
        // Retrieval: Azure AI Search when configured, otherwise BM25 over lesson chunks stored in SQL (no extra cost).
        if (config.GetSection(AzureSearchOptions.Section).Get<AzureSearchOptions>()?.IsConfigured == true)
        {
            services.AddSingleton<ICurriculumRetriever, AzureSearchRetriever>();
        }
        else
        {
            services.AddScoped<ICurriculumRetriever, SqlCurriculumRetriever>();
        }
        services.AddSingleton<IContentSafety, BasicContentSafety>();

        // Email (SMTP when configured, otherwise a logging no-op) — also used by Identity for confirm/reset emails.
        services.Configure<EmailOptions>(config.GetSection(EmailOptions.Section));
        services.AddSingleton<Application.Abstractions.IEmailSender, SmtpEmailSender>();
        services.AddSingleton<Microsoft.AspNetCore.Identity.IEmailSender<Identity.AppUser>, IdentityEmailSender>();

        services.AddSingleton<IClock, SystemClock>();
        return services;
    }
}
