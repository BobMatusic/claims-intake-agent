using System.Diagnostics;
using ClaimsIntake.Core.Extraction;
using ClaimsIntake.Core.Models;
using ClaimsIntake.Core.Policies;
using ClaimsIntake.Core.Services;

namespace ClaimsIntake.Core.Agents;

public class ClaimsAgent
{
    private static readonly ActivitySource ActivitySource = new("ClaimsIntake");

    private readonly CaseFileExtractor _extractor;
    private readonly ClaimEvaluator _evaluator;
    private readonly ExclusionChecker _exclusionChecker;
    private readonly AdjusterSummaryWriter _summaryWriter;

    public ClaimsAgent(
        CaseFileExtractor extractor,
        ClaimEvaluator evaluator,
        ExclusionChecker exclusionChecker,
        AdjusterSummaryWriter summaryWriter)
    {
        _extractor = extractor;
        _evaluator = evaluator;
        _exclusionChecker = exclusionChecker;
        _summaryWriter = summaryWriter;
    }

    public async Task<ClaimAnalysisResult> AnalyzeClaimAsync(
        IReadOnlyList<UploadedDocument> documents,
        Func<ApprovalRequest, Task<bool>> approvalCallback,
        CancellationToken ct = default)
    {
        using var activity = ActivitySource.StartActivity("claim.analyze");
        activity?.SetTag("claim.document_count", documents.Count);

        var extractedDocs = ToExtractedDocuments(documents);
        var extraction = await _extractor.ExtractAsync(extractedDocs, ct);
        var caseFile = CaseFileMapper.Map(extraction);

        if (IsReportEmpty(caseFile.Report))
        {
            var emptyDecision = new ClaimDecision
            {
                Outcome = ClaimOutcome.Escalated,
                Payout = 0m,
                InvoiceTotal = 0m,
                Deductible = 0m,
                Limit = 0m,
                HardBlocks = ["V spise chýba hlásenie poistnej udalosti."],
                SoftSignals = []
            };
            SetFinalTags(activity, emptyDecision, caseFile, 0);
            return await BuildEscalationResultAsync(caseFile, emptyDecision, ct);
        }

        var decision = _evaluator.Evaluate(caseFile);

        if (decision.Outcome != ClaimOutcome.Escalated)
        {
            using var exclusionActivity = ActivitySource.StartActivity("claim.exclusion_check");
            var reportText = documents
                .First(d => d.Type == DocumentType.ClaimReport).ExtractedText ?? string.Empty;
            var exclusions = await _exclusionChecker.CheckAsync(reportText, ct);
            exclusionActivity?.SetTag("claim.exclusion_count", exclusions.Count);

            if (exclusions.Count > 0)
            {
                var exclusionSignals = exclusions
                    .Select(e => $"Možná výluka [{e.ParagraphNumber}]: {e.Reasoning}")
                    .ToList();

                var allSignals = decision.SoftSignals.Concat(exclusionSignals).ToList();
                decision = decision with
                {
                    SoftSignals = allSignals,
                    Exclusions = exclusions,
                    Outcome = allSignals.Count == 0
                        ? ClaimOutcome.AutoApproved
                        : ClaimOutcome.RequiresApproval
                };
            }
        }

        var exclusionCount = decision.Exclusions.Count;
        SetFinalTags(activity, decision, caseFile, exclusionCount);

        if (decision.Outcome == ClaimOutcome.Escalated)
            return await BuildEscalationResultAsync(caseFile, decision, ct);

        return await CreateClaimWithApprovalAsync(caseFile, decision, approvalCallback, ct);
    }

    private async Task<ClaimAnalysisResult> BuildEscalationResultAsync(
        CaseFile caseFile,
        ClaimDecision decision,
        CancellationToken ct)
    {
        var summary = await _summaryWriter.WriteAsync(caseFile, decision, ct);

        return new ClaimAnalysisResult
        {
            Outcome = ClaimOutcome.Escalated,
            Decision = decision,
            CaseFile = caseFile,
            Summary = summary,
            EscalationCaseId = Guid.NewGuid().ToString()
        };
    }

    private async Task<ClaimAnalysisResult> CreateClaimWithApprovalAsync(
        CaseFile caseFile,
        ClaimDecision decision,
        Func<ApprovalRequest, Task<bool>> approvalCallback,
        CancellationToken ct)
    {
        if (decision.Outcome == ClaimOutcome.AutoApproved)
        {
            return new ClaimAnalysisResult
            {
                Outcome = ClaimOutcome.AutoApproved,
                Decision = decision,
                CaseFile = caseFile,
                Summary = $"Nárok automaticky schválený. Plnenie {decision.Payout:N2} €.",
                ClaimId = Guid.NewGuid().ToString()
            };
        }

        bool approved;
        using (var approvalActivity = ActivitySource.StartActivity("claim.await_approval"))
        {
            approved = await approvalCallback(new ApprovalRequest
            {
                ContractNumber = caseFile.Report.ContractNumber,
                IncidentDate = caseFile.Report.IncidentDate,
                Payout = decision.Payout,
                InvoiceTotal = decision.InvoiceTotal,
                Deductible = decision.Deductible,
                Limit = decision.Limit,
                SoftSignals = decision.SoftSignals,
                Exclusions = decision.Exclusions
            });
        }

        return new ClaimAnalysisResult
        {
            Outcome = approved ? ClaimOutcome.Approved : ClaimOutcome.Rejected,
            Decision = decision,
            CaseFile = caseFile,
            Summary = approved
                ? $"Nárok schválený operátorom. Plnenie {decision.Payout:N2} €."
                : "Nárok bol zamietnutý operátorom. Zákazník bude informovaný.",
            ClaimId = approved ? Guid.NewGuid().ToString() : null
        };
    }

    private static void SetFinalTags(Activity? activity, ClaimDecision decision, CaseFile caseFile, int exclusionCount)
    {
        activity?.SetTag("claim.outcome", decision.Outcome.ToString());
        activity?.SetTag("claim.payout", decision.Payout);
        activity?.SetTag("claim.hard_blocks", decision.HardBlocks.Count);
        activity?.SetTag("claim.soft_signals", decision.SoftSignals.Count);
        activity?.SetTag("claim.exclusions_found", exclusionCount);
        activity?.SetTag("claim.invoice_count", caseFile.Invoices.Count);
    }

    private static bool IsReportEmpty(ClaimReport report)
    {
        return string.IsNullOrWhiteSpace(report.ContractNumber)
            && string.IsNullOrWhiteSpace(report.PolicyHolder)
            && report.IncidentDate is null
            && string.IsNullOrWhiteSpace(report.IncidentDescription);
    }

    private static IReadOnlyList<ExtractedDocument> ToExtractedDocuments(IReadOnlyList<UploadedDocument> documents)
    {
        return documents.Select(d => new ExtractedDocument
        {
            FileName = d.FileName,
            Type = d.Type,
            Text = d.ExtractedText ?? string.Empty
        }).ToList();
    }
}
