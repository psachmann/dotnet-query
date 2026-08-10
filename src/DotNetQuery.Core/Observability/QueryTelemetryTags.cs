namespace DotNetQuery.Core.Observability;

internal static class QueryTelemetryTags
{
    // ── Activity names ────────────────────────────────────────────────────────
    internal const string ActivityQueryFetch = "query.fetch";
    internal const string ActivityMutationExecute = "mutation.execute";

    // ── Tag keys ──────────────────────────────────────────────────────────────
    internal const string TagQueryKey = "query.key";
    internal const string TagQueryName = "query.name";
    internal const string TagMutationName = "mutation.name";
    internal const string TagStatus = "status";
    internal const string TagErrorType = "error.type";
    internal const string TagAttempts = "attempts";
    internal const string TagTrigger = "trigger";

    // ── Tag values ────────────────────────────────────────────────────────────
    internal const string StatusSuccess = "success";
    internal const string StatusFailure = "failure";
    internal const string StatusCancelled = "cancelled";

    internal static string ToTagValue(this FetchTrigger trigger) =>
        trigger switch
        {
            FetchTrigger.Manual => "manual",
            FetchTrigger.Invalidate => "invalidate",
            FetchTrigger.Interval => "interval",
            FetchTrigger.Stale => "stale",
            FetchTrigger.Prefetch => "prefetch",
            _ => "unknown",
        };
}
