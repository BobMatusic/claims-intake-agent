using System.Globalization;
using ClaimsIntake.Core.Models;

namespace ClaimsIntake.Core.Extraction;

public static class CaseFileMapper
{
    private static readonly string[] DateFormats =
    [
        "d.M.yyyy", "d. M. yyyy", "dd.MM.yyyy", "dd. MM. yyyy",
        "d.M.yyyy.", "dd.MM.yyyy.", "d. M. yyyy.",
        "yyyy-MM-dd",
        "d/M/yyyy", "dd/MM/yyyy",
        "d. MMMM yyyy", "d. MMMM yyyy.",
    ];

    public static CaseFile Map(CaseFileExtraction extraction)
    {
        var report = extraction.Report is not null
            ? MapReport(extraction.Report)
            : new ClaimReport();

        var invoices = extraction.Invoices
            .Select(MapInvoice)
            .ToList();

        return new CaseFile { Report = report, Invoices = invoices };
    }

    private static ClaimReport MapReport(ClaimReportExtraction r) => new()
    {
        ContractNumber = r.ContractNumber?.Trim(),
        PolicyHolder = r.PolicyHolder?.Trim(),
        IncidentDate = TryParseDate(r.IncidentDateRaw),
        IncidentDateRaw = r.IncidentDateRaw?.Trim(),
        IncidentDescription = r.IncidentDescription?.Trim(),
        VehicleRegistration = NormalizePlate(r.VehicleRegistration),
        ClaimType = r.ClaimType?.Trim().ToUpperInvariant(),
        ContainsSuspiciousInstructions = r.ContainsSuspiciousInstructions
    };

    private static Invoice MapInvoice(InvoiceExtraction inv) => new()
    {
        InvoiceNumber = inv.InvoiceNumber?.Trim(),
        Supplier = inv.Supplier?.Trim(),
        IssueDate = TryParseDate(inv.IssueDateRaw),
        IssueDateRaw = inv.IssueDateRaw?.Trim(),
        Amount = inv.Amount,
        VehicleRegistration = NormalizePlate(inv.VehicleRegistration),
        WorkDescription = inv.WorkDescription?.Trim()
    };

    public static DateOnly? TryParseDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var normalized = raw.Trim().Replace(' ', ' ');
        while (normalized.Contains("  "))
            normalized = normalized.Replace("  ", " ");

        if (DateOnly.TryParseExact(normalized, DateFormats, CultureInfo.GetCultureInfo("sk-SK"),
                DateTimeStyles.None, out var date))
            return date;

        if (DateOnly.TryParseExact(normalized, DateFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out date))
            return date;

        if (DateOnly.TryParse(normalized, CultureInfo.GetCultureInfo("sk-SK"),
                DateTimeStyles.None, out date))
            return date;

        return null;
    }

    private static string? NormalizePlate(string? plate)
    {
        if (string.IsNullOrWhiteSpace(plate))
            return null;

        return plate.Replace(" ", "").Replace("-", "").ToUpperInvariant();
    }
}
