using ClaimsIntake.Core.Policies;

namespace ClaimsIntake.Core.Models;

public enum ClaimOutcome
{
    AutoApproved,
    RequiresApproval,
    Approved,
    Rejected,
    Escalated
}

public record ClaimDecision
{
    public required ClaimOutcome Outcome { get; init; }
    public required decimal Payout { get; init; }
    public required decimal InvoiceTotal { get; init; }
    public required decimal Deductible { get; init; }
    public required decimal Limit { get; init; }
    public required IReadOnlyList<string> HardBlocks { get; init; }
    public required IReadOnlyList<string> SoftSignals { get; init; }
    public IReadOnlyList<ResolvedExclusion> Exclusions { get; init; } = [];
}

public record ClaimAnalysisResult
{
    public required ClaimOutcome Outcome { get; init; }
    public required ClaimDecision Decision { get; init; }
    public required CaseFile CaseFile { get; init; }
    public required string Summary { get; init; }
    public string? ClaimId { get; init; }
    public string? EscalationCaseId { get; init; }
}
