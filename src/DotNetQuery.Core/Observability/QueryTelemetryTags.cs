namespace DotNetQuery.Core.Observability;

internal static class QueryTelemetryTags
{
    // ── Activity names ────────────────────────────────────────────────────────
    internal const string ActivityQueryFetch = "query.fetch";
    internal const string ActivityMutationExecute = "mutation.execute";

    // ── Tag keys ──────────────────────────────────────────────────────────────
    internal const string TagQueryKey = "query.key";
    internal const string TagStatus = "status";
    internal const string TagErrorType = "error.type";

    // ── Tag values ────────────────────────────────────────────────────────────
    internal const string StatusSuccess = "success";
    internal const string StatusFailure = "failure";
}
