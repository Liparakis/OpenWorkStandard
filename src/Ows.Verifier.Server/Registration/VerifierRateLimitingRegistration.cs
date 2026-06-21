using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;

namespace Ows.Verifier.Server;

internal static class VerifierRateLimitingRegistration {
    public const string PublicPolicy = "verifier-public";
    public const string AuthPolicy = "verifier-auth";
    public const string UploadPolicy = "verifier-upload";
    public const string SessionWritePolicy = "verifier-session-write";
    public const string ReadPolicy = "verifier-read";
    public const string DiagnosticsPolicy = "verifier-diagnostics";

    public static void AddVerifierRateLimiting(
        this IServiceCollection services,
        VerifierRateLimitingOptions rateLimitingOptions,
        VerifierStorageOptions storageOptions) {
        services.AddSingleton(rateLimitingOptions);
        services.Configure<FormOptions>(options => {
            var multipartLimit = Math.Max(1024 * 1024, storageOptions.MaxPackageSizeBytes + (1024 * 64));
            options.MultipartBodyLengthLimit = multipartLimit;
            options.MultipartHeadersLengthLimit = 16 * 1024;
        });

        if (!rateLimitingOptions.Enabled) {
            return;
        }

        services.AddRateLimiter(options => {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = static async (context, cancellationToken) => {
                context.HttpContext.Response.ContentType = "application/json";
                await context.HttpContext.Response.WriteAsJsonAsync(new {
                    error = "rate_limit_exceeded",
                    message = "Too many requests. Retry later."
                }, cancellationToken);
            };

            options.AddPolicy(PublicPolicy, httpContext =>
                CreatePartition(httpContext, PublicPolicy, rateLimitingOptions.PublicPermitLimit,
                    rateLimitingOptions.QueueLimit));
            options.AddPolicy(AuthPolicy, httpContext =>
                CreatePartition(httpContext, AuthPolicy, rateLimitingOptions.AuthPermitLimit,
                    rateLimitingOptions.QueueLimit));
            options.AddPolicy(UploadPolicy, httpContext =>
                CreatePartition(httpContext, UploadPolicy, rateLimitingOptions.UploadPermitLimit,
                    rateLimitingOptions.QueueLimit));
            options.AddPolicy(SessionWritePolicy, httpContext =>
                CreatePartition(httpContext, SessionWritePolicy, rateLimitingOptions.SessionWritePermitLimit,
                    rateLimitingOptions.QueueLimit));
            options.AddPolicy(ReadPolicy, httpContext =>
                CreatePartition(httpContext, ReadPolicy, rateLimitingOptions.ReadPermitLimit,
                    rateLimitingOptions.QueueLimit));
            options.AddPolicy(DiagnosticsPolicy, httpContext =>
                CreatePartition(httpContext, DiagnosticsPolicy, rateLimitingOptions.DiagnosticsPermitLimit,
                    rateLimitingOptions.QueueLimit));
        });
    }

    private static RateLimitPartition<string> CreatePartition(
        HttpContext httpContext,
        string policyName,
        int permitLimit,
        int queueLimit) {
        var normalizedPermitLimit = Math.Max(1, permitLimit);
        var normalizedQueueLimit = Math.Max(0, queueLimit);
        var partitionKey = $"{policyName}:{ResolveClientPartitionKey(httpContext)}";
        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions {
            PermitLimit = normalizedPermitLimit,
            Window = TimeSpan.FromMinutes(1),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = normalizedQueueLimit,
            AutoReplenishment = true
        });
    }

    private static string ResolveClientPartitionKey(HttpContext httpContext) {
        if (VerifierAuthorizationHelpers.TryGetAccessContext(httpContext) is { } access) {
            if (!string.IsNullOrWhiteSpace(access.KeyId)) {
                return access.KeyId;
            }

            if (!string.IsNullOrWhiteSpace(access.ActorUserId)) {
                return $"user:{access.ActorUserId}";
            }

            if (!string.IsNullOrWhiteSpace(access.KeyPrefix)) {
                return $"key:{access.KeyPrefix}";
            }
        }

        return httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
    }
}
