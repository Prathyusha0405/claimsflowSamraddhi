namespace ClaimFlow.Contracts;

public sealed record DemoUser(
    string Id, string Email, string Name, string Role, Market? Market, decimal AuthorityLimitUsd, string Blurb);

public sealed record DemoPolicy(
    string PolicyNumber, string ClaimantId, string ClaimantName, Market Market, ClaimType Product,
    string ProductName, string Currency, decimal CoverageLimit, decimal Deductible,
    DateOnly EffectiveFrom, DateOnly EffectiveTo, string Status, string AssetDescription,
    string? VehicleRegistration, string? PropertyAddress);

/// <summary>
/// Seed data used by Identity (users), Policy (policies) and the Claims demo seeder.
/// Lives in Contracts only so the three services agree on the demo world without calling each other during start-up.
/// </summary>
public static class DemoCatalog
{
    public const string DemoPassword = "Demo@123";
    public const decimal Unlimited = 999_999_999m;

    public static readonly IReadOnlyList<DemoUser> Users =
    [
        new("c-sg-001", "mei.tan@claimflow.demo", "Mei Tan", Roles.Claimant, Market.SG, 0, "Singapore motor + home policyholder"),
        new("c-hk-001", "chloe.wong@claimflow.demo", "Chloe Wong", Roles.Claimant, Market.HK, 0, "Hong Kong motor + home policyholder"),
        new("c-my-001", "aisha.rahman@claimflow.demo", "Aisha Rahman", Roles.Claimant, Market.MY, 0, "Malaysia — has a lapsed policy to try"),
        new("c-th-001", "somchai.k@claimflow.demo", "Somchai Kittisak", Roles.Claimant, Market.TH, 0, "Thailand motor + home policyholder"),
        new("c-id-001", "budi.santoso@claimflow.demo", "Budi Santoso", Roles.Claimant, Market.ID, 0, "Indonesia motor + home policyholder"),
        new("c-au-001", "liam.oconnor@claimflow.demo", "Liam O'Connor", Roles.Claimant, Market.AU, 0, "Australia motor + home policyholder"),
        new("o-001", "arjun.nair@claimflow.demo", "Arjun Nair", Roles.ClaimsOfficer, null, 10_000, "Claims officer · approval authority USD 10k"),
        new("o-002", "hana.lim@claimflow.demo", "Hana Lim", Roles.ClaimsOfficer, null, 25_000, "Senior officer · approval authority USD 25k"),
        new("o-003", "daniel.chua@claimflow.demo", "Daniel Chua", Roles.ClaimsOfficer, null, 5_000, "Junior officer · approval authority USD 5k"),
        new("m-001", "sofia.reyes@claimflow.demo", "Sofia Reyes", Roles.Manager, null, Unlimited, "Claims manager · unlimited authority, sees exposure"),
    ];

    public static IReadOnlyList<DemoUser> Claimants => Users.Where(u => u.Role == Roles.Claimant).ToList();
    public static IReadOnlyList<DemoUser> Officers => Users.Where(u => u.Role == Roles.ClaimsOfficer).ToList();

    private sealed record Holder(string UserId, string Vehicle, string Plate, string Address);

    private static readonly Holder[] Holders =
    [
        new("c-sg-001", "2022 Toyota Corolla Altis", "SLX 4821 P", "12 Holland Close #08-14, Singapore 271012"),
        new("c-hk-001", "2021 Honda Fit", "KJ 3892", "Flat 12B, Tower 3, Taikoo Shing, Hong Kong"),
        new("c-my-001", "2020 Proton X70", "VDE 7714", "28 Jalan Bukit Bintang 3, 55100 Kuala Lumpur"),
        new("c-th-001", "2022 Toyota Yaris Ativ", "1AB 2345", "88 Sukhumvit Soi 24, Khlong Tan, Bangkok 10110"),
        new("c-id-001", "2021 Honda HR-V", "B 1234 XYZ", "Jl. Kemang Raya No. 45, Jakarta Selatan"),
        new("c-au-001", "2019 Mazda CX-5", "ABC 12D", "42 Wattle Street, Newtown NSW 2042"),
    ];

    /// <summary>Builds the demo policies relative to <paramref name="today"/> so they never go stale.</summary>
    public static IReadOnlyList<DemoPolicy> BuildPolicies(DateOnly today)
    {
        var list = new List<DemoPolicy>();
        var n = 100200;
        foreach (var h in Holders)
        {
            var user = Users.First(u => u.Id == h.UserId);
            var market = user.Market!.Value;
            var cur = Markets.CurrencyOf(market);
            var code = market.ToString();

            list.Add(new DemoPolicy($"{code}-MTR-{++n}", user.Id, user.Name, market, ClaimType.Motor,
                "Comprehensive Motor", cur, Markets.FromUsd(market, 80_000), Markets.FromUsd(market, 700),
                today.AddDays(-200), today.AddDays(165), "Active", $"{h.Vehicle} — {h.Plate}", h.Plate, null));

            list.Add(new DemoPolicy($"{code}-PRP-{++n}", user.Id, user.Name, market, ClaimType.Property,
                "Home Protect Plus", cur, Markets.FromUsd(market, 350_000), Markets.FromUsd(market, 1_200),
                today.AddDays(-120), today.AddDays(245), "Active", h.Address, null, h.Address));
        }

        // Two policies that exist so intake validation has something to reject.
        var aisha = Users.First(u => u.Id == "c-my-001");
        list.Add(new DemoPolicy("MY-MTR-100099", aisha.Id, aisha.Name, Market.MY, ClaimType.Motor,
            "Comprehensive Motor", "MYR", Markets.FromUsd(Market.MY, 50_000), Markets.FromUsd(Market.MY, 600),
            today.AddDays(-400), today.AddDays(-35), "Expired", "2015 Perodua Myvi — WXY 9981", "WXY 9981", null));
        var mei = Users.First(u => u.Id == "c-sg-001");
        list.Add(new DemoPolicy("SG-MTR-100098", mei.Id, mei.Name, Market.SG, ClaimType.Motor,
            "Comprehensive Motor", "SGD", Markets.FromUsd(Market.SG, 50_000), Markets.FromUsd(Market.SG, 600),
            today.AddDays(-300), today.AddDays(65), "Cancelled", "2016 Honda Jazz — SDD 7712 K", "SDD 7712 K", null));
        return list;
    }
}
