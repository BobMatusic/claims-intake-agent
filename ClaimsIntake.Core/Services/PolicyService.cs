using ClaimsIntake.Core.Models;

namespace ClaimsIntake.Core.Services;

public class PolicyService
{
    public PolicyVerification VerifyPolicy(string? contractNumber, DateOnly? incidentDate)
    {
        if (string.IsNullOrWhiteSpace(contractNumber))
            return new PolicyVerification { ContractNumber = string.Empty, IsActive = false };

        var isActive = contractNumber.StartsWith("SK", StringComparison.OrdinalIgnoreCase);

        return new PolicyVerification
        {
            ContractNumber = contractNumber,
            IsActive = isActive,
            CoveredFrom = isActive ? new DateOnly(2020, 1, 1) : null,
            CoveredUntil = isActive ? new DateOnly(2030, 12, 31) : null,
            Limit = 8_000m,
            Deductible = 100m
        };
    }

    public ClaimsHistory GetClaimsHistory(string? contractNumber)
    {
        return new ClaimsHistory
        {
            ContractNumber = contractNumber ?? string.Empty,
            ClaimsInLastYear = 0
        };
    }
}
