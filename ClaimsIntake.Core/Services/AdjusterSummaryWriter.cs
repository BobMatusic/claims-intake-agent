using ClaimsIntake.Core.Models;
using Microsoft.Extensions.AI;

namespace ClaimsIntake.Core.Services;

public class AdjusterSummaryWriter
{
    private readonly IChatClient _chatClient;

    public AdjusterSummaryWriter(IChatClient chatClient)
    {
        _chatClient = chatClient;
    }

    public async Task<string> WriteAsync(
        CaseFile caseFile,
        ClaimDecision decision,
        CancellationToken ct = default)
    {
        var reasons = decision.HardBlocks.Count > 0 ? decision.HardBlocks : decision.SoftSignals;

        var prompt = $"""
            Napíš stručné zhrnutie poistného spisu pre likvidátora. Rozsah 5–8 viet, vecný tón,
            bez oslovení a bez odporúčaní, ktoré nevyplývajú z uvedených dát.

            Spis:
            - zmluva: {caseFile.Report.ContractNumber}
            - poistník: {caseFile.Report.PolicyHolder}
            - dátum udalosti: {caseFile.Report.IncidentDate:d.M.yyyy}
            - vozidlo: {caseFile.Report.VehicleRegistration}
            - popis: {caseFile.Report.IncidentDescription}
            - faktúry: {caseFile.Invoices.Count}, spolu {decision.InvoiceTotal:N2} €

            Dôvody, prečo spis nebol spracovaný automaticky — prevezmi ich presne, nič nepridávaj:
            {string.Join("\n", reasons.Select(r => "- " + r))}

            Na záver uveď jednou vetou, čo má likvidátor overiť ako prvé.
            """;

        var response = await _chatClient.GetResponseAsync(prompt, cancellationToken: ct);

        var calculation = $"""

            Návrh plnenia:
              faktúry spolu      {decision.InvoiceTotal,10:N2} €
              limit zmluvy       {decision.Limit,10:N2} €
              spoluúčasť       − {decision.Deductible,10:N2} €
              ─────────────────────────────
              navrhované plnenie {decision.Payout,10:N2} €
            """;

        return response.Text + calculation;
    }
}
