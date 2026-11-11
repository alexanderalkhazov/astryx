using Astryx.SharedKernel.Results;
using Microsoft.AspNetCore.Http;

namespace Astryx.Infrastructure.Http;

/// <summary>Maps stable error codes to HTTP status codes (SYSTEM_DESIGN.md §11.2).</summary>
public static class ErrorHttpMapping
{
    private static readonly Dictionary<string, int> StatusByCode = new(StringComparer.Ordinal)
    {
        ["bad_request"] = 400,
        ["unauthorized"] = 401, ["token_expired"] = 401, ["session_revoked"] = 401, ["invalid_credentials"] = 401, ["refresh_token_superseded"] = 401,
        ["forbidden"] = 403, ["email_not_verified"] = 403, ["live_trading_disabled"] = 403,
        ["not_found"] = 404, ["instrument_not_found"] = 404,
        ["conflict"] = 409, ["duplicate"] = 409, ["request_in_progress"] = 409, ["proposal_not_pending"] = 409,
        ["assessment_stale"] = 409, ["quote_stale"] = 409, ["position_not_open"] = 409, ["concurrency_conflict"] = 409,
        ["market_closed"] = 409,
        ["precondition_failed"] = 412,
        ["validation_failed"] = 422, ["risk_check_failed"] = 422, ["idempotency_key_reused"] = 422,
        ["insufficient_history"] = 422, ["current_password_incorrect"] = 422, ["ai_levels_rejected"] = 422, ["invalid_or_expired_token"] = 422,
        ["account_locked"] = 423,
        ["rate_limited"] = 429, ["ai_quota_exceeded"] = 429,
        ["market_data_unavailable"] = 503, ["scanner_unavailable"] = 503, ["ai_provider_unavailable"] = 503,
    };

    private static readonly Dictionary<int, string> DefaultCodeByStatus = new()
    {
        [400] = "bad_request", [401] = "unauthorized", [403] = "forbidden", [404] = "not_found",
        [405] = "method_not_allowed", [409] = "conflict", [412] = "precondition_failed", [413] = "payload_too_large",
        [415] = "unsupported_media_type", [422] = "validation_failed", [423] = "account_locked", [429] = "rate_limited",
        [500] = "internal_error", [503] = "service_unavailable",
    };

    public static int StatusFor(string code) => StatusByCode.GetValueOrDefault(code, StatusCodes.Status400BadRequest);

    public static string DefaultCodeFor(int status) => DefaultCodeByStatus.GetValueOrDefault(status, status >= 500 ? "internal_error" : "bad_request");

    public static string TypeUri(string code) => $"https://docs.astryx.app/errors/{code}";

    /// <summary>Converts an expected failure into an RFC 7807 response.</summary>
    public static IResult ToProblem(this Error error) =>
        Results.Problem(
            statusCode: StatusFor(error.Code),
            title: error.Code.Replace('_', ' '),
            detail: error.Message,
            type: TypeUri(error.Code),
            extensions: new Dictionary<string, object?> { ["code"] = error.Code });
}
