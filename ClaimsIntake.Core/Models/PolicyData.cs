using System.Text.Json;

namespace ClaimsIntake.Core.Models;

public record PolicyData
{
    public IReadOnlyList<PolicyRecord> Policies { get; init; } = [];
    public IReadOnlyList<ClaimRecord> Claims { get; init; } = [];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static PolicyData LoadFromFile(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<PolicyData>(json, JsonOptions) ?? new PolicyData();
    }
}

public record PolicyRecord
{
    public string ContractNumber { get; init; } = "";
    public string PolicyHolder { get; init; } = "";
    public string Status { get; init; } = "";
    public DateOnly? CoveredFrom { get; init; }
    public DateOnly? CoveredUntil { get; init; }
    public string Product { get; init; } = "";
    public IReadOnlyList<string> CoveredClaimTypes { get; init; } = [];
    public decimal Limit { get; init; }
    public decimal Deductible { get; init; }
    public IReadOnlyList<VehicleRecord> Vehicles { get; init; } = [];

    public bool IsActive => string.Equals(Status, "aktívna", StringComparison.OrdinalIgnoreCase);
}

public record VehicleRecord
{
    public string Registration { get; init; } = "";
    public string Make { get; init; } = "";
    public string Model { get; init; } = "";
    public int Year { get; init; }
    public string Vin { get; init; } = "";
}

public record ClaimRecord
{
    public string ClaimId { get; init; } = "";
    public string ContractNumber { get; init; } = "";
    public string VehicleRegistration { get; init; } = "";
    public DateOnly? IncidentDate { get; init; }
    public string ClaimType { get; init; } = "";
    public string Description { get; init; } = "";
    public string RepairShop { get; init; } = "";
    public string InvoiceNumber { get; init; } = "";
    public decimal Amount { get; init; }
    public string Status { get; init; } = "";
}
