using System.Text.RegularExpressions;

namespace Astryx.Modules.Ai.Application.Validation;

/// <summary>Phrases that imply a guaranteed outcome (SYSTEM_DESIGN.md §12.7 step 7). The text is replaced, not edited.</summary>
public static partial class ContentPolicy
{
    public const string Fallback = "[Removed: this text implied a guaranteed outcome.]";

    public static bool Violates(string text) => Guarantees().IsMatch(text);

    [GeneratedRegex(
        @"\b(guarantee[ds]?|guaranteeing|risk[-\s]?free|no[-\s]risk|(can'?t|cannot|can not|won'?t) (lose|fail|go wrong)|100\s?% (sure|certain|safe)|sure thing|certain to (rise|win|double|go up)|will (definitely|certainly) (rise|go up|double|win)|free money|easy money)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Guarantees();
}
