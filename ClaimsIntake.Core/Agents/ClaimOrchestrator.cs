using System.Diagnostics;
using System.Text.Json;
using ClaimsIntake.Core.Agents.Models;
using ClaimsIntake.Core.Models;
using ClaimsIntake.Core.Services;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace ClaimsIntake.Core.Agents;

public class ClaimOrchestrator
{
    private static readonly ActivitySource ActivitySource = new("ClaimsIntake");

    private readonly InvestigationPipelineClient _investigationClient;
    private readonly IChatClient _chatClient;
    private readonly AdjusterSummaryWriter _summaryWriter;

    public ClaimOrchestrator(
        InvestigationPipelineClient investigationClient,
        IChatClient chatClient,
        AdjusterSummaryWriter summaryWriter)
    {
        _investigationClient = investigationClient;
        _chatClient = chatClient;
        _summaryWriter = summaryWriter;
    }

    public async Task<ClaimAnalysisResult> RunAsync(
        IReadOnlyList<UploadedDocument> documents,
        Func<ApprovalRequest, CaseContext, Task<bool>> approvalCallback,
        CancellationToken ct = default)
    {
        using var activity = ActivitySource.StartActivity("claim.orchestrate");

        var photos = documents.Where(d => d.Type == DocumentType.Photo).ToList();
        var nonPhotos = documents.Where(d => d.Type != DocumentType.Photo).ToList();

        activity?.SetTag("orchestrator.photo_count", photos.Count);
        activity?.SetTag("orchestrator.document_count", nonPhotos.Count);

        _investigationClient.SetDocuments(nonPhotos);

        var investigationAgent = new ChatClientAgent(
            _investigationClient,
            new ChatClientAgentOptions
            {
                Name = "Investigation",
                Description = "Analyzes claim documents, extracts case data, evaluates claim, and checks policy exclusions.",
                UseProvidedChatClientAsIs = true
            });

        var agents = new List<AIAgent> { investigationAgent };
        var hasPhotos = photos.Count > 0 && nonPhotos.Any(d => d.Type == DocumentType.Invoice);

        if (hasPhotos)
        {
            var photoAgent = new ChatClientAgent(
                _chatClient,
                new ChatClientAgentOptions
                {
                    Name = "PhotoVerification",
                    Description = "Compares uploaded photos against invoice items to detect suspicious or unverifiable charges.",
                    ChatOptions = new ChatOptions
                    {
                        ResponseFormat = ChatResponseFormat.ForJsonSchema<InvoiceVerificationResult>()
                    }
                });
            agents.Add(photoAgent);
        }

        var workflow = AgentWorkflowBuilder.BuildConcurrent(
            "ClaimAnalysis",
            agents,
            aggregator: null);

        var workflowAgent = workflow.AsAIAgent(
            "claim-workflow",
            "ClaimWorkflowAgent",
            "Concurrent claim analysis: investigation + photo verification");

        var inputMessages = BuildInputMessages(nonPhotos, photos);

        activity?.SetTag("orchestrator.agent_count", agents.Count);

        var response = await workflowAgent.RunAsync(inputMessages, cancellationToken: ct);

        var (investigationResult, photoResult) = ParseWorkflowResponse(response, hasPhotos);

        if (photoResult is not null)
        {
            activity?.SetTag("photo.items_total", photoResult.Items.Count);
            activity?.SetTag("photo.confirmed", photoResult.Items.Count(i => i.Verdict == ItemVerdict.Confirmed));
            activity?.SetTag("photo.suspicious", photoResult.Items.Count(i => i.Verdict == ItemVerdict.Suspicious));
            activity?.SetTag("photo.unverifiable", photoResult.Items.Count(i => i.Verdict == ItemVerdict.Unverifiable));
        }

        if (investigationResult is null)
        {
            activity?.SetTag("claim.outcome", "Escalated");
            activity?.SetTag("claim.payout", 0);
            return new ClaimAnalysisResult
            {
                Outcome = ClaimOutcome.Escalated,
                Decision = new ClaimDecision
                {
                    Outcome = ClaimOutcome.Escalated,
                    Payout = 0m,
                    InvoiceTotal = 0m,
                    Deductible = 0m,
                    Limit = 0m,
                    HardBlocks = ["Zlyhala analýza dokumentov."],
                    SoftSignals = []
                },
                CaseFile = new CaseFile { Report = new ClaimReport() },
                Summary = "Analýza zlyhala.",
                EscalationCaseId = Guid.NewGuid().ToString()
            };
        }

        var decision = investigationResult.Decision;
        var caseFile = investigationResult.CaseFile;

        if (photoResult is not null)
            decision = MergePhotoFindings(decision, photoResult);

        activity?.SetTag("claim.outcome", decision.Outcome.ToString());
        activity?.SetTag("claim.payout", decision.Payout);
        activity?.SetTag("claim.invoice_total", decision.InvoiceTotal);
        activity?.SetTag("claim.soft_signals", decision.SoftSignals.Count);
        activity?.SetTag("claim.hard_blocks", decision.HardBlocks.Count);

        if (decision.Outcome == ClaimOutcome.Escalated)
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

        return await CreateClaimWithApprovalAsync(caseFile, decision, approvalCallback, ct);
    }

    private static List<ChatMessage> BuildInputMessages(
        IReadOnlyList<UploadedDocument> nonPhotos,
        IReadOnlyList<UploadedDocument> photos)
    {
        var messages = new List<ChatMessage>();
        var contents = new List<AIContent>();
        contents.Add(new TextContent(PhotoVerificationAgent.BuildPrompt(
            string.Join("\n\n", nonPhotos
                .Where(d => d.Type == DocumentType.Invoice)
                .Select(d => d.ExtractedText ?? string.Empty)),
            nonPhotos
                .FirstOrDefault(d => d.Type == DocumentType.ClaimReport)?.ExtractedText ?? string.Empty)));

        foreach (var photo in photos)
        {
            var ext = Path.GetExtension(photo.FileName).ToLowerInvariant();
            var mediaType = ext switch
            {
                ".png" => "image/png",
                _ => "image/jpeg"
            };
            contents.Add(new DataContent(photo.Content, mediaType));
        }

        messages.Add(new ChatMessage(ChatRole.User, contents));
        return messages;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private static (InvestigationResult?, InvoiceVerificationResult?) ParseWorkflowResponse(
        AgentResponse response,
        bool hasPhotoAgent)
    {
        InvestigationResult? investigation = null;
        InvoiceVerificationResult? photo = null;

        foreach (var message in response.Messages)
        {
            if (message.Role != ChatRole.Assistant || string.IsNullOrWhiteSpace(message.Text))
                continue;

            var text = message.Text;
            if (investigation is null && text.Contains("\"CaseFile\"", StringComparison.OrdinalIgnoreCase))
            {
                investigation = JsonSerializer.Deserialize<InvestigationResult>(text, JsonOptions);
            }
            else if (hasPhotoAgent && photo is null && text.Contains("\"items\"", StringComparison.OrdinalIgnoreCase))
            {
                photo = JsonSerializer.Deserialize<InvoiceVerificationResult>(text, JsonOptions);
            }
        }

        return (investigation, photo);
    }

    private static ClaimDecision MergePhotoFindings(
        ClaimDecision decision,
        InvoiceVerificationResult photoResult)
    {
        var photoSignals = new List<string>();

        foreach (var item in photoResult.Items)
        {
            switch (item.Verdict)
            {
                case ItemVerdict.Suspicious:
                    photoSignals.Add($"[Foto] Podozrivá položka: {item.ItemDescription} — {item.Reasoning}");
                    break;
                case ItemVerdict.Unverifiable:
                    photoSignals.Add($"[Foto] Neoveriteľná položka: {item.ItemDescription} — {item.Reasoning}");
                    break;
            }
        }

        if (photoSignals.Count == 0)
            return decision;

        var mergedSignals = decision.SoftSignals.Concat(photoSignals).ToList();
        return decision with
        {
            SoftSignals = mergedSignals,
            Outcome = mergedSignals.Count > 0 || decision.HardBlocks.Count > 0
                ? ClaimOutcome.RequiresApproval
                : decision.Outcome
        };
    }

    private async Task<ClaimAnalysisResult> CreateClaimWithApprovalAsync(
        CaseFile caseFile,
        ClaimDecision decision,
        Func<ApprovalRequest, CaseContext, Task<bool>> approvalCallback,
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
            var request = new ApprovalRequest
            {
                ContractNumber = caseFile.Report.ContractNumber,
                IncidentDate = caseFile.Report.IncidentDate,
                Payout = decision.Payout,
                InvoiceTotal = decision.InvoiceTotal,
                Deductible = decision.Deductible,
                Limit = decision.Limit,
                HardBlocks = decision.HardBlocks,
                SoftSignals = decision.SoftSignals,
                Exclusions = decision.Exclusions
            };
            var context = CaseContext.FromCaseFile(caseFile, decision);
            approved = await approvalCallback(request, context);
            approvalActivity?.SetTag("approval.result", approved ? "Approved" : "Rejected");
            approvalActivity?.SetTag("approval.payout", decision.Payout);
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
}
