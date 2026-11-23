using System.Text.Json.Nodes;

namespace Astryx.Modules.Ai.Application.Validation;

/// <summary>Outcome of <see cref="AiOutputValidator"/> (SYSTEM_DESIGN.md §12.7).</summary>
public enum ValidationStatus
{
    /// <summary>Every check passed unchanged.</summary>
    Valid,

    /// <summary>Usable, but items were dropped or replaced, or the levels were rejected.</summary>
    ValidWithRejections,

    /// <summary>Unusable: the job fails with <c>ai_output_invalid</c>.</summary>
    Invalid,
}

/// <summary>Issue codes recorded with a result.</summary>
public static class ValidationIssueCodes
{
    public const string ParseError = "parse_error";
    public const string SchemaError = "schema_error";
    public const string IdentityMismatch = "identity_mismatch";
    public const string Stale = "stale";
    public const string UngroundedIndicator = "ungrounded_indicator";
    public const string UngroundedCatalyst = "ungrounded_catalyst";
    public const string UngroundedNumber = "ungrounded_number";
    public const string LevelsRejected = "levels_rejected";
    public const string PolicyViolation = "policy_violation";
    public const string ForbiddenField = "forbidden_field";
    public const string PlaceholderDropped = "placeholder_dropped";
}

/// <summary>One finding, at a JSON pointer into the model output.</summary>
public sealed record ValidationIssue(string Code, string Path, string Message);

/// <summary>What <see cref="LevelValidator"/> decided about <c>suggestedLevels</c>.</summary>
/// <param name="RewardRisk">(target − entry high) ÷ (entry high − stop), 2 dp, when the order is coherent.</param>
public sealed record LevelValidation(bool Accepted, IReadOnlyList<string> Reasons, decimal? RewardRisk);

/// <summary>
/// The validated analysis: the model output after dropping and replacing, plus the backend fields (<c>rewardRisk</c>,
/// <c>levelValidation</c>, <c>stale</c>, <c>model</c>, <c>promptVersion</c>, <c>disclaimer</c>). <see cref="Analysis"/>
/// is null when <see cref="Status"/> is <see cref="ValidationStatus.Invalid"/>.
/// </summary>
/// <param name="Repairable">Invalid at parse or schema, so one repair retry may help (§12.7 steps 1–2).</param>
public sealed record ValidationResult(ValidationStatus Status, JsonObject? Analysis, IReadOnlyList<ValidationIssue> Issues, LevelValidation? Levels, bool Stale, bool Repairable)
{
    public string? FirstError => Issues.FirstOrDefault()?.Message;
}
