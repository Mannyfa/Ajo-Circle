namespace AjoCircle.Domain.Common;

/// <summary>
/// Money is always integer minor units (kobo for NGN) plus an explicit ISO 4217 currency code.
/// Never use floating point for money (brief §7.4).
/// </summary>
public readonly record struct Money
{
    public long AmountMinor { get; }
    public string Currency { get; }

    public Money(long amountMinor, string currency)
    {
        if (amountMinor < 0)
            throw new DomainException("money_negative", "Amounts cannot be negative.");
        if (!Currencies.IsSupported(currency))
            throw new DomainException("currency_unsupported", $"Currency '{currency}' is not supported.");
        AmountMinor = amountMinor;
        Currency = currency;
    }

    public static Money Ngn(long amountMinor) => new(amountMinor, Currencies.Ngn);

    /// <summary>Multiplies with overflow checking; an overflow is a rule violation, not a silent wrap.</summary>
    public Money Times(long factor)
    {
        if (factor < 0) throw new DomainException("money_negative", "Multiplier cannot be negative.");
        try { return new Money(checked(AmountMinor * factor), Currency); }
        catch (OverflowException) { throw new DomainException("money_overflow", "Amount is outside the supported range."); }
    }

    public override string ToString() => $"{Currency} {AmountMinor / 100}.{AmountMinor % 100:D2}";
}

public static class Currencies
{
    public const string Ngn = "NGN";

    /// <summary>MVP is NGN-only (multi-currency is a non-goal). Every stored amount still carries its code.</summary>
    public static bool IsSupported(string? code) => code == Ngn;

    public const int NgnMinorUnitsPerMajor = 100;
}
