using System.Text.RegularExpressions;

namespace Astryx.ArchitectureTests;

/// <summary>
/// Floating point is banned for financial logic (SYSTEM_DESIGN.md §20.1, TASKS.md T-004).
/// BannedApiAnalyzers (src/BannedSymbols.Money.txt) catches banned methods, but not the
/// <c>double</c>/<c>float</c> keywords or built-in decimal casts, so module sources are scanned here.
/// A line may opt out only with a justification comment: <c>// float-ok: reason</c>.
/// </summary>
public partial class FloatingPointBanTests
{
    [Fact]
    public void Module_and_shared_kernel_sources_do_not_use_floating_point()
    {
        var root = RepositoryRoot();
        var sources = Directory.EnumerateFiles(Path.Combine(root, "src", "Modules"), "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "src", "BuildingBlocks", "Astryx.SharedKernel"), "*.cs", SearchOption.AllDirectories))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

        var violations = (
            from path in sources
            from line in File.ReadLines(path).Select((text, index) => (text, number: index + 1))
            where !line.text.Contains("float-ok:", StringComparison.Ordinal)
            let code = StripCommentsAndStrings(line.text)
            where FloatingPointPattern().IsMatch(code)
            select $"{Path.GetRelativePath(root, path)}:{line.number}: {line.text.Trim()}").ToList();

        violations.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("double price = 1.0;", true)]
    [InlineData("var x = (double)amount;", true)]
    [InlineData("float f = 1.5f;", true)]
    [InlineData("System.Double d = 0;", true)]
    [InlineData("var y = 3.0d;", true)]
    [InlineData("decimal price = 186.50m;", false)]
    [InlineData("// a double-check comment", false)]
    [InlineData("var s = \"double\";", false)]
    public void Pattern_detects_floating_point_usage(string line, bool expected) =>
        FloatingPointPattern().IsMatch(StripCommentsAndStrings(line)).ShouldBe(expected);

    private static string StripCommentsAndStrings(string line)
    {
        var withoutStrings = StringLiteral().Replace(line, "\"\"");
        var commentStart = withoutStrings.IndexOf("//", StringComparison.Ordinal);
        return commentStart >= 0 ? withoutStrings[..commentStart] : withoutStrings;
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Astryx.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Astryx.slnx not found above the test output directory.");
    }

    [GeneratedRegex(@"\b(double|float|Double|Single)\b|\b\d+(\.\d+)?[dDfF]\b")]
    private static partial Regex FloatingPointPattern();

    [GeneratedRegex("\"(?:[^\"\\\\]|\\\\.)*\"")]
    private static partial Regex StringLiteral();
}
