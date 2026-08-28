using System.ComponentModel;
using ClaimsIntake.Core.Models;
using ClaimsIntake.Core.Services;

namespace ClaimsIntake.Core.Agents.Tools;

public class PolicyTools
{
    private readonly PolicyService _policyService;
    private readonly CaseContext _context;

    public PolicyTools(PolicyService policyService, CaseContext context)
    {
        _policyService = policyService;
        _context = context;
    }

    [Description("Vráti detail zmluvy podľa čísla: platnosť od–do, stav (aktívna, zaniknutá, nezaplatená), rozsah krytia, spoluúčasť a limit.")]
    public string GetPolicyDetail(
        [Description("Číslo zmluvy, napr. SK1234567890.")] string contractNumber)
    {
        var policy = _policyService.FindPolicy(contractNumber);

        if (policy is null)
            return $"Zmluva {contractNumber} nebola nájdená.";

        var vehicles = policy.Vehicles.Count > 0
            ? string.Join("\n", policy.Vehicles.Select(v =>
                $"  - {v.Registration} | {v.Make} {v.Model} {v.Year} | VIN: {v.Vin}"))
            : "  žiadne";

        return $"""
            Zmluva: {policy.ContractNumber}
            Poistník: {policy.PolicyHolder}
            Stav: {policy.Status}
            Produkt: {policy.Product}
            Platnosť: {policy.CoveredFrom:d.M.yyyy} – {policy.CoveredUntil:d.M.yyyy}
            Krytie: {string.Join(", ", policy.CoveredClaimTypes)}
            Limit: {policy.Limit:N2} €
            Spoluúčasť: {policy.Deductible:N2} €
            Vozidlá:
            {vehicles}
            """;
    }
}
