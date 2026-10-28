namespace ClaimFlow.Contracts;

public sealed record MarketInfo(Market Code, string Name, string Currency, decimal UsdRate);

/// <summary>
/// Static market metadata. FX rates are fixed demo values — a real system would read a rates service.
/// Exposure is always tracked in the market's own currency and only normalised to USD for roll-ups.
/// </summary>
public static class Markets
{
    public static readonly IReadOnlyDictionary<Market, MarketInfo> All = new Dictionary<Market, MarketInfo>
    {
        [Market.SG] = new(Market.SG, "Singapore", "SGD", 0.74m),
        [Market.HK] = new(Market.HK, "Hong Kong", "HKD", 0.128m),
        [Market.MY] = new(Market.MY, "Malaysia", "MYR", 0.225m),
        [Market.TH] = new(Market.TH, "Thailand", "THB", 0.030m),
        [Market.ID] = new(Market.ID, "Indonesia", "IDR", 0.000061m),
        [Market.AU] = new(Market.AU, "Australia", "AUD", 0.66m),
    };

    public static MarketInfo Get(Market market) => All[market];

    public static string CurrencyOf(Market market) => All[market].Currency;

    public static decimal ToUsd(Market market, decimal amount) => Math.Round(amount * All[market].UsdRate, 2);

    /// <summary>Converts USD to local currency, rounded to 2 significant digits so seed data looks natural.</summary>
    public static decimal FromUsd(Market market, decimal usd)
    {
        var local = usd / All[market].UsdRate;
        if (local <= 0) return 0;
        var magnitude = (decimal)Math.Pow(10, Math.Floor(Math.Log10((double)local)) - 1);
        return Math.Round(local / magnitude) * magnitude;
    }
}
