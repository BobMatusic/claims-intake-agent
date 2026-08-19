using ClaimsIntake.Core.Policies;

namespace ClaimsIntake.Core.Models;

public record ApprovalRequest
{
    public required string? ContractNumber { get; init; }
    public required DateOnly? IncidentDate { get; init; }
    public required decimal Payout { get; init; }
    public required decimal InvoiceTotal { get; init; }
    public required decimal Deductible { get; init; }
    public required decimal Limit { get; init; }
    public required IReadOnlyList<string> SoftSignals { get; init; }
    public IReadOnlyList<ResolvedExclusion> Exclusions { get; init; } = [];
}
