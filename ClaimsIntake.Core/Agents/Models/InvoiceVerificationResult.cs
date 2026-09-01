namespace ClaimsIntake.Core.Agents.Models;

public record InvoiceVerificationResult
{
    public required IReadOnlyList<LineItemVerdict> Items { get; init; }
    public required string Summary { get; init; }
}

public record LineItemVerdict
{
    public required string ItemDescription { get; init; }
    public required ItemVerdict Verdict { get; init; }
    public required string Reasoning { get; init; }
}

public enum ItemVerdict
{
    Confirmed,
    Suspicious,
    Unverifiable
}
