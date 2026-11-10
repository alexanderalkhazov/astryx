namespace Astryx.SharedKernel.Finance;

public sealed class CurrencyMismatchException(Currency left, Currency right)
    : InvalidOperationException($"Cannot combine {left} and {right} amounts.");
