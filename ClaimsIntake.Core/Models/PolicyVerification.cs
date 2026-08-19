namespace ClaimsIntake.Core.Models;

public record PolicyVerification
{
    public required string ContractNumber { get; init; }
    public required bool IsActive { get; init; }
    public DateOnly? CoveredFrom { get; init; }
    public DateOnly? CoveredUntil { get; init; }
    public IReadOnlyList<string> CoveredClaimTypes { get; init; } = ["AUTO", "DOMACNOST", "INE"];
    public decimal Limit { get; init; } = 8_000m;
    public decimal Deductible { get; init; } = 100m;

    public bool CoversDate(DateOnly date)
    {
        if (!IsActive) return false;
        if (CoveredFrom is not null && date < CoveredFrom) return false;
        if (CoveredUntil is not null && date > CoveredUntil) return false;
        return true;
    }

    public bool CoversClaimType(string? claimType)
    {
        if (string.IsNullOrWhiteSpace(claimType)) return true;
        return CoveredClaimTypes.Contains(claimType, StringComparer.OrdinalIgnoreCase);
    }
}

public record ClaimsHistory
{
    public required string ContractNumber { get; init; }
    public int ClaimsInLastYear { get; init; }
    public decimal TotalPayoutsLastYear { get; init; }
    public IReadOnlyList<DateOnly> ClaimDates { get; init; } = [];

    public bool HasClaimOn(DateOnly date) => ClaimDates.Contains(date);
    public int CountWithinMonths(int months) => ClaimsInLastYear;
}
