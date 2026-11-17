using System.Text.RegularExpressions;

namespace Astryx.ArchitectureTests;

/// <summary>
/// Secrets and personal data never go into log templates (SYSTEM_DESIGN.md §18.15, TASKS.md T-115). Rather than
/// redacting after the fact, no structured-logging placeholder may name a password, token, key, credential or email.
/// Request and response bodies are not logged, and <c>Microsoft.AspNetCore</c> logs at Warning, so query strings
/// (the SignalR <c>access_token</c>) never reach the log either.
/// </summary>
public partial class SensitiveLoggingTests
{
    [Fact]
    public void No_log_template_names_a_secret_or_personal_field()
    {
        var root = RepositoryRoot();
        var sources = new[] { Path.Combine(root, "src"), Path.Combine(root, "apps", "api") }
            .SelectMany(dir => Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

        var violations = (
            from path in sources
            from line in File.ReadLines(path).Select((text, index) => (text, number: index + 1))
            where LogCall().IsMatch(line.text) && SensitivePlaceholder().IsMatch(line.text)
            select $"{Path.GetRelativePath(root, path)}:{line.number}: {line.text.Trim()}").ToList();

        violations.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("logger.LogInformation(\"Signed in {Email}\", email);", true)]
    [InlineData("logger.LogWarning(\"Refresh failed for {RefreshToken}\", token);", true)]
    [InlineData("logger.LogError(ex, \"Bad {ApiKey}\", key);", true)]
    [InlineData("logger.LogInformation(\"Job {JobId} succeeded\", id);", false)]
    [InlineData("logger.LogInformation(\"Revoked {Count} push token(s)\", n);", false)]
    public void Pattern_flags_sensitive_placeholders(string line, bool expected) =>
        (LogCall().IsMatch(line) && SensitivePlaceholder().IsMatch(line)).ShouldBe(expected);

    [GeneratedRegex(@"\.Log(Trace|Debug|Information|Warning|Error|Critical)\(")]
    private static partial Regex LogCall();

    [GeneratedRegex(@"\{(Password|NewPassword|CurrentPassword|Token|AccessToken|RefreshToken|PushToken|ApiKey|UserKey|SecretKey|KeyId|Secret|Credentials?|Authorization|Email)\}", RegexOptions.IgnoreCase)]
    private static partial Regex SensitivePlaceholder();

    internal static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Astryx.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Astryx.slnx not found above the test output directory.");
    }
}
