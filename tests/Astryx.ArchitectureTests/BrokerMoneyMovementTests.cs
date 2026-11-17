namespace Astryx.ArchitectureTests;

/// <summary>
/// The app never moves money (SYSTEM_DESIGN.md §17.3 design rule 6, TASKS.md T-140): no source file may name an Alpaca
/// funding, transfer, journal or Broker API path. The Trading API client has no such method, and this keeps it that way.
/// </summary>
public class BrokerMoneyMovementTests
{
    private static readonly string[] Forbidden =
    [
        "/transfers", "/journals", "ach_relationships", "/recipient_banks", "/wallets", "/funding", "/v1/accounts", "broker-api.",
    ];

    [Fact]
    public void No_source_file_names_a_money_movement_or_Broker_API_path()
    {
        var root = SensitiveLoggingTests.RepositoryRoot();
        var violations = new[] { Path.Combine(root, "src"), Path.Combine(root, "apps", "api") }
            .SelectMany(dir => Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(path => File.ReadLines(path).Select((text, index) => (path, text, number: index + 1)))
            .Where(line => Forbidden.Any(f => line.text.Contains(f, StringComparison.OrdinalIgnoreCase)))
            .Select(line => $"{Path.GetRelativePath(root, line.path)}:{line.number}: {line.text.Trim()}")
            .ToList();

        violations.ShouldBeEmpty();
    }
}
