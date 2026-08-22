namespace ClaimsIntake.Core.Models;

public record CaseFile
{
    public required ClaimReport Report { get; init; }
    public IReadOnlyList<Invoice> Invoices { get; init; } = [];
}

public record ClaimReport
{
    public string? ContractNumber { get; init; }
    public string? PolicyHolder { get; init; }
    public DateOnly? IncidentDate { get; init; }
    public string? IncidentDescription { get; init; }
    public string? VehicleRegistration { get; init; }
    public string? ClaimType { get; init; }
    public bool ContainsSuspiciousInstructions { get; init; }
}

public record Invoice
{
    public string? InvoiceNumber { get; init; }
    public DateOnly? IssueDate { get; init; }
    public decimal? Amount { get; init; }
    public string? VehicleRegistration { get; init; }
}
