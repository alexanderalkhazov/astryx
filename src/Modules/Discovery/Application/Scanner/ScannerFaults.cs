namespace Astryx.Modules.Discovery.Application.Scanner;

/// <summary>
/// Prototype control (SYSTEM_DESIGN.md §11.11, T-111): while <see cref="Failing"/> is set, scanner runs fail as an
/// unexpected error would (<c>SCN-500</c>), so Discover shows its scanner-error state. Set only through the
/// Development-only <c>/dev/faults</c>.
/// </summary>
public sealed class ScannerFaults
{
    public volatile bool Failing;
}
