using System.ComponentModel;
using ClaimsIntake.Core.Models;
using ClaimsIntake.Core.Services;

namespace ClaimsIntake.Core.Agents.Tools;

public class ClaimsTools
{
    private readonly PolicyService _policyService;
    private readonly CaseContext _context;

    public ClaimsTools(PolicyService policyService, CaseContext context)
    {
        _policyService = policyService;
        _context = context;
    }

    [Description("Vráti zoznam nárokov evidovaných na zmluve za posledných N mesiacov. Každý záznam obsahuje: dátum, typ škody, sumu a stav.")]
    public string GetClaimsHistory(
        [Description("Číslo zmluvy.")] string contractNumber,
        [Description("Koľko mesiacov dozadu vyhľadávať.")] int months)
    {
        var claims = _policyService.GetClaimsForContract(contractNumber, months);

        if (claims.Count == 0)
            return $"Na zmluve {contractNumber} nie sú evidované žiadne nároky za posledných {months} mesiacov.";

        var lines = claims.Select(c =>
            $"- {c.ClaimId} | {c.IncidentDate:d.M.yyyy} | {c.ClaimType} | {c.Amount:N2} € | {c.Status}");

        return $"Nároky na zmluve {contractNumber} ({claims.Count}):\n{string.Join("\n", lines)}";
    }

    [Description("Vráti kompletný detail konkrétneho nároku: čo sa opravovalo, ktorý servis, číslo faktúry a výška plnenia.")]
    public string GetClaimDetail(
        [Description("Identifikátor nároku vrátený z GetClaimsHistory.")] string claimId)
    {
        var claim = _policyService.GetClaimById(claimId);

        if (claim is null)
            return $"Nárok {claimId} nebol nájdený.";

        return $"""
            Nárok: {claim.ClaimId}
            Zmluva: {claim.ContractNumber}
            ŠPZ: {claim.VehicleRegistration}
            Dátum udalosti: {claim.IncidentDate:d.M.yyyy}
            Typ: {claim.ClaimType}
            Popis: {claim.Description}
            Servis: {claim.RepairShop}
            Faktúra: {claim.InvoiceNumber}
            Suma: {claim.Amount:N2} €
            Stav: {claim.Status}
            """;
    }

    [Description("Vyhľadá všetky nároky evidované na dané vozidlo naprieč všetkými zmluvami. Užitočné, keď vozidlo mohlo medzitým zmeniť majiteľa alebo zmluvu.")]
    public string FindClaimsByVehicle(
        [Description("ŠPZ vozidla, napr. BA123AB.")] string registration)
    {
        var claims = _policyService.GetClaimsByVehicle(registration);

        if (claims.Count == 0)
            return $"Na vozidlo {registration} nie sú evidované žiadne nároky.";

        var lines = claims.Select(c =>
            $"- {c.ClaimId} | {c.ContractNumber} | {c.IncidentDate:d.M.yyyy} | {c.Amount:N2} € | {c.Status}");

        return $"Nároky na vozidlo {registration} ({claims.Count}):\n{string.Join("\n", lines)}";
    }
}
