namespace ClaimsIntake.Core.Models;

public record CaseContext
{
    public required string? ContractNumber { get; init; }
    public required string? PolicyHolder { get; init; }
    public required DateOnly? IncidentDate { get; init; }
    public required string? IncidentDescription { get; init; }
    public required string? VehicleRegistration { get; init; }
    public required decimal Payout { get; init; }
    public required decimal InvoiceTotal { get; init; }
    public required decimal Deductible { get; init; }
    public required decimal Limit { get; init; }
    public required ClaimOutcome Outcome { get; init; }
    public required IReadOnlyList<string> HardBlocks { get; init; }
    public required IReadOnlyList<string> SoftSignals { get; init; }
    public required IReadOnlyList<Policies.ResolvedExclusion> Exclusions { get; init; }

    private const int MaxDescriptionLength = 500;

    public static CaseContext FromCaseFile(CaseFile caseFile, ClaimDecision decision)
    {
        var description = caseFile.Report.IncidentDescription;
        if (description is not null && description.Length > MaxDescriptionLength)
            description = description[..MaxDescriptionLength] + "...";

        return new CaseContext
        {
            ContractNumber = caseFile.Report.ContractNumber,
            PolicyHolder = caseFile.Report.PolicyHolder,
            IncidentDate = caseFile.Report.IncidentDate,
            IncidentDescription = description,
            VehicleRegistration = caseFile.Report.VehicleRegistration,
            Payout = decision.Payout,
            InvoiceTotal = decision.InvoiceTotal,
            Deductible = decision.Deductible,
            Limit = decision.Limit,
            Outcome = decision.Outcome,
            HardBlocks = decision.HardBlocks,
            SoftSignals = decision.SoftSignals,
            Exclusions = decision.Exclusions
        };
    }
}
