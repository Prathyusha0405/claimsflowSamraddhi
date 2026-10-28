namespace ClaimFlow.Contracts;

/// <summary>Policy terms captured on the claim at the moment of submission (a snapshot — avoids runtime coupling).</summary>
public sealed record PolicyTerms(
    string ProductName,
    string Currency,
    decimal CoverageLimit,
    decimal Deductible,
    DateOnly EffectiveFrom,
    DateOnly EffectiveTo,
    string AssetDescription);

/// <summary>Response of the synchronous coverage check the Claims service makes to the Policy service.</summary>
public sealed record CoverageCheckResult(
    bool Eligible,
    string? Code,
    string? Reason,
    string? PolicyNumber,
    string? ClaimantId,
    string? ClaimantName,
    Market? Market,
    PolicyTerms? Terms);
