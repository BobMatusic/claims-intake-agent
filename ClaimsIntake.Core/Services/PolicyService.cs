using ClaimsIntake.Core.Models;

namespace ClaimsIntake.Core.Services;

public class PolicyService
{
    private readonly IReadOnlyList<PolicyRecord> _policies;
    private readonly IReadOnlyList<ClaimRecord> _claims;

    public PolicyService(PolicyData data)
    {
        _policies = data.Policies;
        _claims = data.Claims;
    }

    public PolicyVerification VerifyPolicy(string? contractNumber, DateOnly? incidentDate)
    {
        if (string.IsNullOrWhiteSpace(contractNumber))
            return new PolicyVerification { ContractNumber = string.Empty, IsActive = false };

        var policy = FindPolicy(contractNumber);
        if (policy is null)
            return new PolicyVerification { ContractNumber = contractNumber, IsActive = false };

        return new PolicyVerification
        {
            ContractNumber = policy.ContractNumber,
            IsActive = policy.IsActive,
            CoveredFrom = policy.CoveredFrom,
            CoveredUntil = policy.CoveredUntil,
            CoveredClaimTypes = policy.CoveredClaimTypes.ToList(),
            Limit = policy.Limit,
            Deductible = policy.Deductible
        };
    }

    public ClaimsHistory GetClaimsHistory(string? contractNumber)
    {
        if (string.IsNullOrWhiteSpace(contractNumber))
            return new ClaimsHistory { ContractNumber = string.Empty };

        var claims = _claims
            .Where(c => string.Equals(c.ContractNumber, contractNumber, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var oneYearAgo = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(-12);
        var recentClaims = claims.Where(c => c.IncidentDate >= oneYearAgo).ToList();

        return new ClaimsHistory
        {
            ContractNumber = contractNumber,
            ClaimsInLastYear = recentClaims.Count,
            ClaimDates = claims
                .Where(c => c.IncidentDate.HasValue)
                .Select(c => c.IncidentDate!.Value)
                .ToList()
        };
    }

    public PolicyRecord? FindPolicy(string contractNumber)
    {
        return _policies.FirstOrDefault(p =>
            string.Equals(p.ContractNumber, contractNumber, StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<ClaimRecord> GetClaimsForContract(string contractNumber, int months)
    {
        var cutoff = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(-months);
        return _claims
            .Where(c => string.Equals(c.ContractNumber, contractNumber, StringComparison.OrdinalIgnoreCase)
                         && c.IncidentDate >= cutoff)
            .ToList();
    }

    public ClaimRecord? GetClaimById(string claimId)
    {
        return _claims.FirstOrDefault(c =>
            string.Equals(c.ClaimId, claimId, StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<ClaimRecord> GetClaimsByVehicle(string registration)
    {
        return _claims
            .Where(c => string.Equals(c.VehicleRegistration, registration, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
}
