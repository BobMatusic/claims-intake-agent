using ClaimsIntake.Core.Agents.Interfaces;
using ClaimsIntake.Core.Agents.Tools;
using ClaimsIntake.Core.Models;
using ClaimsIntake.Core.Policies;
using ClaimsIntake.Core.Services;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace ClaimsIntake.Core.Agents.Factory;

public class ClaimAssistantFactory : IClaimAssistantFactory
{
    private readonly IChatClient _chatClient;
    private readonly PolicyService _policyService;
    private readonly PolicyConditionsSearch _search;

    public ClaimAssistantFactory(
        IChatClient chatClient,
        PolicyService policyService,
        PolicyConditionsSearch search)
    {
        _chatClient = chatClient;
        _policyService = policyService;
        _search = search;
    }

    public async Task<IClaimAssistant> CreateAsync(
        CaseContext caseContext,
        CancellationToken ct = default)
    {
        const string name = "claim-assistant";

        var instructions = BuildInstructions(caseContext);
        var tools = BuildTools(_policyService, _search, caseContext);

        var agent = new ChatClientAgent(
            _chatClient,
            instructions: instructions,
            name: name,
            tools: tools);

        var session = await agent.CreateSessionAsync(ct);

        return new SingleAgentAssistant(agent, session, name);
    }

    /// <summary>
    /// Every tool the assistant is given. All of them are read-only lookups: the assistant can
    /// find information, never change a claim. That boundary is the real guarantee — the system
    /// prompt only asks the model not to decide, this list makes deciding impossible.
    /// Covered by ClaimAssistantFactoryTests.
    /// </summary>
    internal static IList<AITool> BuildTools(
        PolicyService policyService,
        PolicyConditionsSearch search,
        CaseContext caseContext)
    {
        var policyTools = new PolicyTools(policyService, caseContext);
        var claimsTools = new ClaimsTools(policyService, caseContext);
        var conditionsTools = new ConditionsTools(search);

        return
        [
            AIFunctionFactory.Create(policyTools.GetPolicyDetail),
            AIFunctionFactory.Create(claimsTools.GetClaimsHistory),
            AIFunctionFactory.Create(claimsTools.GetClaimDetail),
            AIFunctionFactory.Create(claimsTools.FindClaimsByVehicle),
            AIFunctionFactory.Create(conditionsTools.SearchPolicyConditions),
        ];
    }

    private static string BuildInstructions(CaseContext ctx)
    {
        var exclusionText = ctx.Exclusions.Count > 0
            ? string.Join("\n", ctx.Exclusions.Select(e => $"  - [{e.ParagraphNumber}] {e.Reasoning}"))
            : "  žiadne";

        var signalText = ctx.SoftSignals.Count > 0
            ? string.Join("\n", ctx.SoftSignals.Select(s => $"  - {s}"))
            : "  žiadne";

        var hardBlockText = ctx.HardBlocks.Count > 0
            ? string.Join("\n", ctx.HardBlocks.Select(b => $"  - {b}"))
            : "  žiadne";

        return $"""
            Si asistent likvidátora poistných udalostí.

            Odpovedáš na otázky o otvorenom spise. Ak odpoveď máš v kontexte,
            nevolaj žiadny nástroj. Ak ju nemáš, vyber si nástroj, ktorý ju vie dohľadať.

            Pravidlá:
            - Pri každom údaji uveď zdroj — číslo zmluvy, dátum nároku alebo číslo článku.
            - Ak odpoveď nevieš zistiť, povedz to. Nedomýšľaj si.
            - Nerozhoduješ o nároku. Schvaľuje a zamieta výhradne likvidátor.
            - Odpovedaj stručne a vecne po slovensky.
            - Neponúkaj akcie, ktoré nevieš vykonať. Vieš len vyhľadávať údaje
              pomocou svojich nástrojov — nevieš upravovať spis, meniť údaje,
              odosielať správy ani vykonávať žiadne iné operácie.
              Na konci odpovede neponúkaj zoznam možností ani ďalšie kroky ktore nevieš spraviť.

            Aktuálny spis:
            - Zmluva: {ctx.ContractNumber}
            - Poistník: {ctx.PolicyHolder}
            - Dátum udalosti: {ctx.IncidentDate?.ToString("d.M.yyyy") ?? "neznámy"}
            - ŠPZ: {ctx.VehicleRegistration ?? "neuvedená"}
            - Popis: {ctx.IncidentDescription ?? "neuvedený"}
            - Výsledok: {ctx.Outcome}
            - Faktúry spolu: {ctx.InvoiceTotal:N2} €
            - Spoluúčasť: {ctx.Deductible:N2} €
            - Limit zmluvy: {ctx.Limit:N2} €
            - Navrhované plnenie: {ctx.Payout:N2} €

            Blokujúce problémy:
            {hardBlockText}

            Výluky:
            {exclusionText}

            Signály:
            {signalText}
            """;
    }
}
