using System.Diagnostics;
using ClaimsIntake.Core.Models;

namespace ClaimsIntake.Core.Services;

public class ClaimEvaluator
{
    private static readonly ActivitySource ActivitySource = new("ClaimsIntake");

    private readonly PolicyService _policyService;
    private readonly DecisionEngine _decisionEngine;

    public ClaimEvaluator(PolicyService policyService, DecisionEngine decisionEngine)
    {
        _policyService = policyService;
        _decisionEngine = decisionEngine;
    }

    public ClaimDecision Evaluate(CaseFile caseFile)
    {
        using var activity = ActivitySource.StartActivity("claim.checks");

        var policy = _policyService.VerifyPolicy(caseFile.Report.ContractNumber, caseFile.Report.IncidentDate);
        var history = _policyService.GetClaimsHistory(caseFile.Report.ContractNumber);
        var decision = _decisionEngine.Evaluate(caseFile, policy, history);

        activity?.SetTag("claim.hard_block_count", decision.HardBlocks.Count);
        activity?.SetTag("claim.soft_signal_count", decision.SoftSignals.Count);
        activity?.SetTag("claim.payout", decision.Payout);

        return decision;
    }
}
