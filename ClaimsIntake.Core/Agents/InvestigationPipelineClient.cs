using System.Diagnostics;
using System.Text.Json;
using ClaimsIntake.Core.Extraction;
using ClaimsIntake.Core.Models;
using ClaimsIntake.Core.Policies;
using ClaimsIntake.Core.Services;
using Microsoft.Extensions.AI;

namespace ClaimsIntake.Core.Agents;

public sealed class InvestigationPipelineClient : IChatClient
{
    private static readonly ActivitySource ActivitySource = new("ClaimsIntake");

    private readonly CaseFileExtractor _extractor;
    private readonly ClaimEvaluator _evaluator;
    private readonly ExclusionChecker _exclusionChecker;

    private IReadOnlyList<UploadedDocument>? _documents;

    public InvestigationPipelineClient(
        CaseFileExtractor extractor,
        ClaimEvaluator evaluator,
        ExclusionChecker exclusionChecker)
    {
        _extractor = extractor;
        _evaluator = evaluator;
        _exclusionChecker = exclusionChecker;
    }

    public void SetDocuments(IReadOnlyList<UploadedDocument> documents) =>
        _documents = documents;

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (_documents is null || _documents.Count == 0)
            return new ChatResponse([new ChatMessage(ChatRole.Assistant, "{}")]);

        using var activity = ActivitySource.StartActivity("claim.analyze");
        activity?.SetTag("claim.document_count", _documents.Count);

        var extractedDocs = _documents.Select(d => new ExtractedDocument
        {
            FileName = d.FileName,
            Type = d.Type,
            Text = d.ExtractedText ?? string.Empty
        }).ToList();

        var extraction = await _extractor.ExtractAsync(extractedDocs, cancellationToken);
        var caseFile = CaseFileMapper.Map(extraction);

        if (IsReportEmpty(caseFile.Report))
        {
            var emptyResult = new InvestigationResult
            {
                CaseFile = caseFile,
                Decision = new ClaimDecision
                {
                    Outcome = ClaimOutcome.Escalated,
                    Payout = 0m,
                    InvoiceTotal = 0m,
                    Deductible = 0m,
                    Limit = 0m,
                    HardBlocks = ["V spise chýba hlásenie poistnej udalosti."],
                    SoftSignals = []
                }
            };
            return ToResponse(emptyResult);
        }

        var decision = _evaluator.Evaluate(caseFile);

        using var exclusionActivity = ActivitySource.StartActivity("claim.exclusion_check");
        var reportText = _documents
            .First(d => d.Type == DocumentType.ClaimReport).ExtractedText ?? string.Empty;
        var exclusions = await _exclusionChecker.CheckAsync(reportText, cancellationToken);
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
                Outcome = (decision.HardBlocks.Count > 0 || allSignals.Count > 0)
                    ? ClaimOutcome.RequiresApproval
                    : ClaimOutcome.AutoApproved
            };
        }

        activity?.SetTag("claim.outcome", decision.Outcome.ToString());
        activity?.SetTag("claim.payout", decision.Payout);
        activity?.SetTag("claim.hard_blocks", decision.HardBlocks.Count);
        activity?.SetTag("claim.soft_signals", decision.SoftSignals.Count);

        var result = new InvestigationResult { CaseFile = caseFile, Decision = decision };
        return ToResponse(result);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(chatMessages, options, cancellationToken);
        var text = response.Messages.FirstOrDefault()?.Text ?? "{}";
        yield return new ChatResponseUpdate(ChatRole.Assistant, text);
    }

    public void Dispose() { }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    private static ChatResponse ToResponse(InvestigationResult result)
    {
        var json = JsonSerializer.Serialize(result);
        return new ChatResponse([new ChatMessage(ChatRole.Assistant, json)]);
    }

    private static bool IsReportEmpty(ClaimReport report) =>
        string.IsNullOrWhiteSpace(report.ContractNumber)
        && string.IsNullOrWhiteSpace(report.PolicyHolder)
        && report.IncidentDate is null
        && string.IsNullOrWhiteSpace(report.IncidentDescription);
}

public record InvestigationResult
{
    public required CaseFile CaseFile { get; init; }
    public required ClaimDecision Decision { get; init; }
}
