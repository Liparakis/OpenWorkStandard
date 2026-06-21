using System.Text;
using Ows.Core.Education;
using Ows.Core.Notarization;

namespace Ows.Verifier.Server;

/// <summary>
/// Provides route endpoint mapping extension methods for Prometheus metrics and basic health checks.
/// </summary>
internal static class VerifierMetricsEndpoints {
    /// <summary>
    /// Maps health check (`/health`) and metrics (`/metrics`) endpoints.
    /// </summary>
    /// <param name="app">The route builder application instance.</param>
    /// <returns>The route builder with endpoints mapped.</returns>
    public static void MapVerifierMetricsEndpoints(this IEndpointRouteBuilder app) {
        app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }))
            .RequireRateLimiting(VerifierRateLimitingRegistration.PublicPolicy);

        app.MapGet("/metrics",
            async (HttpContext context, IVerifierAuditStore auditStore, IPackageVerificationJobStore jobStore,
                IVerifierStorage storage, IEducationStore educationStore, IPackageBlobStore blobStore,
                VerifierStorageOptions options, CancellationToken cancellationToken) => {
                    var summary = await auditStore.GetSummaryAsync(cancellationToken);
                    var jobSummary = await jobStore.GetSummaryAsync(cancellationToken);

                    var storageReady = false;
                    try {
                        storageReady = await storage.CheckHealthAsync(cancellationToken);
                    } catch {
                        /*ignored*/
                    }

                    bool educationReady = false;
                    try {
                        educationReady =
                            await VerifierServerHelpers.CheckEducationStoreReadyAsync(educationStore, cancellationToken);
                    } catch {
                        /*ignored*/
                    }

                    bool packageStorageReady = false;
                    try {
                        packageStorageReady = await blobStore.CheckHealthAsync(cancellationToken);
                    } catch {
                        /*ignored*/
                    }

                    var signingConfigured = !string.IsNullOrWhiteSpace(options.ReceiptSigningKey);

                    var auditTotalSum = summary.SessionsCreated +
                                        summary.CheckpointsAccepted +
                                        summary.HeartbeatsAccepted +
                                        summary.PackagesSubmitted +
                                        summary.PackagesVerified +
                                        summary.ReportsRead +
                                        summary.AuthFailures +
                                        summary.AccessDenied;

                    var sb = new StringBuilder();
                    sb.AppendLine("# HELP ows_sessions_created_total Total number of OWS assessment sessions created.");
                    sb.AppendLine("# TYPE ows_sessions_created_total counter");
                    sb.AppendLine($"ows_sessions_created_total {summary.SessionsCreated}");
                    sb.AppendLine();

                    sb.AppendLine("# HELP ows_checkpoints_accepted_total Total number of checkpoints accepted.");
                    sb.AppendLine("# TYPE ows_checkpoints_accepted_total counter");
                    sb.AppendLine($"ows_checkpoints_accepted_total {summary.CheckpointsAccepted}");
                    sb.AppendLine();

                    sb.AppendLine("# HELP ows_heartbeats_accepted_total Total number of heartbeats accepted.");
                    sb.AppendLine("# TYPE ows_heartbeats_accepted_total counter");
                    sb.AppendLine($"ows_heartbeats_accepted_total {summary.HeartbeatsAccepted}");
                    sb.AppendLine();

                    sb.AppendLine("# HELP ows_package_uploads_total Total number of package uploads.");
                    sb.AppendLine("# TYPE ows_package_uploads_total counter");
                    sb.AppendLine($"ows_package_uploads_total {summary.PackagesSubmitted}");
                    sb.AppendLine();

                    sb.AppendLine(
                        "# HELP ows_package_verification_successes_total Total number of successful package verification completions.");
                    sb.AppendLine("# TYPE ows_package_verification_successes_total counter");
                    sb.AppendLine($"ows_package_verification_successes_total {summary.PackagesVerified}");
                    sb.AppendLine();

                    sb.AppendLine(
                        "# HELP ows_package_verification_failures_total Total number of failed package verification attempts.");
                    sb.AppendLine("# TYPE ows_package_verification_failures_total counter");
                    sb.AppendLine($"ows_package_verification_failures_total {summary.PackageVerificationFailures}");
                    sb.AppendLine();

                    sb.AppendLine(
                        "# HELP ows_package_verification_jobs_total Total number of package verification jobs by status.");
                    sb.AppendLine("# TYPE ows_package_verification_jobs_total gauge");
                    sb.AppendLine($"ows_package_verification_jobs_total{{status=\"pending\"}} {jobSummary.Pending}");
                    sb.AppendLine($"ows_package_verification_jobs_total{{status=\"running\"}} {jobSummary.Running}");
                    sb.AppendLine($"ows_package_verification_jobs_total{{status=\"succeeded\"}} {jobSummary.Succeeded}");
                    sb.AppendLine($"ows_package_verification_jobs_total{{status=\"failed\"}} {jobSummary.Failed}");
                    sb.AppendLine();

                    sb.AppendLine("# HELP ows_auth_failures_total Total number of API key authentication failures.");
                    sb.AppendLine("# TYPE ows_auth_failures_total counter");
                    sb.AppendLine($"ows_auth_failures_total {summary.AuthFailures}");
                    sb.AppendLine();

                    sb.AppendLine("# HELP ows_access_denied_total Total number of forbidden access attempts.");
                    sb.AppendLine("# TYPE ows_access_denied_total counter");
                    sb.AppendLine($"ows_access_denied_total {summary.AccessDenied}");
                    sb.AppendLine();

                    sb.AppendLine("# HELP ows_audit_events_total Sum of all recorded audit events.");
                    sb.AppendLine("# TYPE ows_audit_events_total counter");
                    sb.AppendLine($"ows_audit_events_total {auditTotalSum}");
                    sb.AppendLine();

                    sb.AppendLine(
                        "# HELP ows_ready_dependency_status Health readiness status of OWS verifier dependencies (1 = healthy, 0 = unhealthy).");
                    sb.AppendLine("# TYPE ows_ready_dependency_status gauge");
                    sb.AppendLine($"ows_ready_dependency_status{{dependency=\"storage\"}} {(storageReady ? 1 : 0)}");
                    sb.AppendLine($"ows_ready_dependency_status{{dependency=\"education\"}} {(educationReady ? 1 : 0)}");
                    sb.AppendLine(
                        $"ows_ready_dependency_status{{dependency=\"packages\"}} {(packageStorageReady ? 1 : 0)}");
                    sb.AppendLine($"ows_ready_dependency_status{{dependency=\"signing\"}} {(signingConfigured ? 1 : 0)}");
                    sb.AppendLine();

                    sb.AppendLine(
                        "# HELP ows_package_verification_worker_enabled Whether this verifier instance runs the in-process package verification worker (1 = enabled, 0 = disabled).");
                    sb.AppendLine("# TYPE ows_package_verification_worker_enabled gauge");
                    sb.AppendLine($"ows_package_verification_worker_enabled {(options.PackageWorkerEnabled ? 1 : 0)}");
                    sb.AppendLine();

                    sb.AppendLine("# HELP ows_instance_mode Verifier instance mode (1 = current mode label).");
                    sb.AppendLine("# TYPE ows_instance_mode gauge");
                    sb.AppendLine(
                        $"ows_instance_mode{{mode=\"{VerifierServerHelpers.DescribeInstanceMode(options.PackageWorkerEnabled)}\"}} 1");

                    return Results.Content(sb.ToString(), "text/plain; version=0.0.4; charset=utf-8");
                })
            .RequireRateLimiting(VerifierRateLimitingRegistration.PublicPolicy);
    }
}
