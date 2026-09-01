using System.ClientModel;
using System.Diagnostics;
using System.Text;
using ClaimsIntake.Core;
using ClaimsIntake.Core.Agents;
using ClaimsIntake.Core.Agents.Interfaces;
using ClaimsIntake.Core.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.FeatureManagement;
using Microsoft.JSInterop;

namespace ClaimsIntake.Web.Components.Pages;

public partial class Home : IAsyncDisposable
{
    [Inject] private AzureDocumentIntelligence DocIntel { get; set; } = default!;
    [Inject] private ClaimOrchestrator Orchestrator { get; set; } = default!;
    [Inject] private IFeatureManager FeatureManager { get; set; } = default!;
    [Inject] private IClaimAssistantFactory AssistantFactory { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;

    private static readonly ActivitySource ActivitySource = new("ClaimsIntake");

    private readonly List<UploadedDocument> uploadedFiles = [];
    private string? error;
    private bool agentEnabled;

    private bool isProcessing;
    private bool approvalPending;
    private ApprovalRequest? pendingRequest;
    private ClaimAnalysisResult? analysisResult;
    private TaskCompletionSource<bool>? approvalTcs;

    private const long MaxFileSize = 5 * 1024 * 1024;
    private const int MaxFiles = 10;

    private static readonly string[] AllowedTypes = ["image/jpeg", "image/png", "application/pdf"];

    private string? previewPhotoUri;
    private string? previewPhotoName;

    private IClaimAssistant? assistant;
    private readonly List<ChatBubble> chatMessages = [];
    private string chatInput = "";
    private bool isChatProcessing;

    private record ChatBubble(string Text, bool IsUser, IReadOnlyList<string> ToolsUsed)
    {
        public ChatBubble(string text, bool isUser) : this(text, isUser, []) { }
    }

    protected override async Task OnInitializedAsync()
    {
        agentEnabled = await FeatureManager.IsEnabledAsync("AgentEnabled");
    }

    private async Task OnFilesSelected(InputFileChangeEventArgs e)
    {
        error = null;
        analysisResult = null;

        var files = e.GetMultipleFiles(MaxFiles + uploadedFiles.Count);

        if (uploadedFiles.Count + files.Count > MaxFiles)
        {
            error = $"Maximálny počet súborov je {MaxFiles}.";
            return;
        }

        foreach (var file in files)
        {
            if (!AllowedTypes.Contains(file.ContentType))
            {
                error = $"Súbor {file.Name}: povolené sú len JPG, PNG alebo PDF.";
                return;
            }
            if (file.Size > MaxFileSize)
            {
                error = $"Súbor {file.Name} je príliš veľký (max 5 MB).";
                return;
            }

            using var ms = new MemoryStream();
            await file.OpenReadStream(MaxFileSize).CopyToAsync(ms);

            var docType = InferDocumentType(file.Name)
                ?? (uploadedFiles.Any(f => f.Type == DocumentType.ClaimReport)
                    ? DocumentType.Invoice
                    : DocumentType.ClaimReport);

            uploadedFiles.Add(new UploadedDocument
            {
                FileName = file.Name,
                Content = ms.ToArray(),
                Type = docType
            });
        }
    }

    private static DocumentType? InferDocumentType(string fileName)
    {
        var name = fileName.ToLowerInvariant();
        if (name.Contains("hlasenie") || name.Contains("hlásenie") || name.Contains("udalost") || name.Contains("udalosť"))
            return DocumentType.ClaimReport;
        if (name.Contains("faktura") || name.Contains("faktúra") || name.Contains("invoice"))
            return DocumentType.Invoice;
        if (name.Contains("foto") || name.Contains("photo") || name.Contains("img") || name.Contains("snimk"))
            return DocumentType.Photo;
        return null;
    }

    private void RemoveFile(UploadedDocument file) => uploadedFiles.Remove(file);

    private async Task ProcessAsync()
    {
        if (!await FeatureManager.IsEnabledAsync("AgentEnabled"))
        {
            error = "Automatické spracovanie je dočasne nedostupné. "
                  + "Udalosť bola zaevidovaná na manuálne spracovanie.";
            return;
        }

        if (uploadedFiles.Count == 0) return;

        var reportCount = uploadedFiles.Count(f => f.Type == DocumentType.ClaimReport);
        var invoiceCount = uploadedFiles.Count(f => f.Type == DocumentType.Invoice);

        if (reportCount != 1)
        {
            error = "Musí byť práve jedno hlásenie.";
            return;
        }

        if (invoiceCount < 1)
        {
            error = "Musí byť aspoň jedna faktúra.";
            return;
        }

        using var activity = ActivitySource.StartActivity("claim.process");
        activity?.SetTag("claim.file_count", uploadedFiles.Count);

        isProcessing = true;
        analysisResult = null;
        error = null;
        await InvokeAsync(StateHasChanged);

        try
        {
            var extractionTasks = uploadedFiles
                .Where(doc => doc.Type != DocumentType.Photo)
                .Select(async doc =>
                {
                    var result = await DocIntel.AnalyzeAsync(doc.Content);
                    doc.ExtractedText = result.FullText;
                });
            await Task.WhenAll(extractionTasks);

            analysisResult = await Orchestrator.RunAsync(uploadedFiles, RequestApprovalAsync);
        }
        catch (ClientResultException ex) when (ex.Status == 400 && ex.Message.Contains("content_filter"))
        {
            error = "Dokument bol zamietnutý bezpečnostnou kontrolou a nebol spracovaný. "
                  + "Prosím skontrolujte jeho obsah alebo kontaktujte podporu.";
            Console.WriteLine($"[ClaimsIntake SECURITY] Content filter triggered: {ex}");
        }
        catch (Exception ex)
        {
            error = $"Spracovanie zlyhalo: {ex.Message}";
            Console.WriteLine($"[ClaimsIntake ERROR] {ex}");
        }
        finally
        {
            isProcessing = false;
        }
    }

    private async Task<bool> RequestApprovalAsync(ApprovalRequest request, CaseContext caseContext)
    {
        pendingRequest = request;
        approvalPending = true;
        approvalTcs = new TaskCompletionSource<bool>();
        chatMessages.Clear();

        try
        {
            assistant = await AssistantFactory.CreateAsync(caseContext);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ClaimsIntake] Failed to create assistant: {ex.Message}");
        }

        await InvokeAsync(StateHasChanged);
        return await approvalTcs.Task;
    }

    private void OpenPhotoPreview(string dataUri, string fileName)
    {
        previewPhotoUri = dataUri;
        previewPhotoName = fileName;
    }

    private void ClosePhotoPreview()
    {
        previewPhotoUri = null;
        previewPhotoName = null;
    }

    private async void Resolve(bool approved)
    {
        approvalPending = false;
        pendingRequest = null;
        chatMessages.Clear();

        if (assistant is not null)
        {
            await assistant.DisposeAsync();
            assistant = null;
        }

        approvalTcs?.SetResult(approved);
    }

    private async Task SendChatAsync()
    {
        if (string.IsNullOrWhiteSpace(chatInput) || assistant is null) return;

        var question = chatInput.Trim();
        chatInput = "";
        chatMessages.Add(new ChatBubble(question, true));
        isChatProcessing = true;
        await InvokeAsync(StateHasChanged);
        await ScrollChatAsync();

        try
        {
            var reply = await assistant.AskAsync(question);
            chatMessages.Add(new ChatBubble(reply.Text, false, reply.ToolsUsed));
        }
        catch (Exception ex)
        {
            chatMessages.Add(new ChatBubble($"Chyba: {ex.Message}", false));
        }
        finally
        {
            isChatProcessing = false;
            await InvokeAsync(StateHasChanged);
            await ScrollChatAsync();
        }
    }

    private async Task ChatKeyDownAsync(KeyboardEventArgs e)
    {
        if (e.Key == "Enter" && !isChatProcessing && !string.IsNullOrWhiteSpace(chatInput))
            await SendChatAsync();
    }

    private async Task ScrollChatAsync()
    {
        await JS.InvokeVoidAsync("scrollToBottom", "chat-scroll");
    }

    public async ValueTask DisposeAsync()
    {
        if (assistant is not null)
            await assistant.DisposeAsync();
    }

    private async Task DownloadSummaryAsync()
    {
        if (analysisResult is null) return;

        var md = BuildSummaryMarkdown(analysisResult);
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(md));
        var fileId = analysisResult.ClaimId ?? analysisResult.EscalationCaseId ?? "spis";
        await JS.InvokeVoidAsync("downloadFileFromBytes", $"zhrnutie_{fileId}.md", base64);
    }

    private static string BuildSummaryMarkdown(ClaimAnalysisResult result)
    {
        var d = result.Decision;
        var sb = new StringBuilder();

        sb.AppendLine("# Zhrnutie poistného spisu");
        sb.AppendLine();
        sb.AppendLine($"**Zmluva:** {result.CaseFile.Report.ContractNumber}");
        sb.AppendLine($"**Poistník:** {result.CaseFile.Report.PolicyHolder}");
        sb.AppendLine($"**Dátum udalosti:** {result.CaseFile.Report.IncidentDate?.ToString("d.M.yyyy") ?? "—"}");

        var outcomeText = result.Outcome switch
        {
            ClaimOutcome.AutoApproved => "Automaticky schválený",
            ClaimOutcome.Approved => "Schválený operátorom",
            ClaimOutcome.Rejected => "Zamietnutý operátorom",
            ClaimOutcome.Escalated => "Eskalovaný",
            _ => result.Outcome.ToString()
        };
        sb.AppendLine($"**Výsledok:** {outcomeText}");

        if (result.ClaimId is not null)
            sb.AppendLine($"**Nárok:** {result.ClaimId}");
        if (result.EscalationCaseId is not null)
            sb.AppendLine($"**Eskalačný spis:** {result.EscalationCaseId}");

        sb.AppendLine();
        sb.AppendLine("## Návrh plnenia");
        sb.AppendLine();
        sb.AppendLine($"| | |");
        sb.AppendLine($"|---|---:|");
        sb.AppendLine($"| Faktúry spolu | {d.InvoiceTotal:N2} € |");
        sb.AppendLine($"| Limit zmluvy | {d.Limit:N2} € |");
        sb.AppendLine($"| Spoluúčasť | − {d.Deductible:N2} € |");
        sb.AppendLine($"| **Navrhované plnenie** | **{d.Payout:N2} €** |");

        if (d.HardBlocks.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Blokujúce problémy");
            sb.AppendLine();
            foreach (var block in d.HardBlocks)
                sb.AppendLine($"- {block}");
        }

        if (d.Exclusions.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Nájdené výluky z podmienok");
            sb.AppendLine();
            foreach (var ex in d.Exclusions)
            {
                sb.AppendLine($"### [{ex.ParagraphNumber}] {ex.ArticleTitle}");
                sb.AppendLine();
                sb.AppendLine(ex.Reasoning);
                sb.AppendLine();
                sb.Append("**Dokaz z hlasenia:** ").Append(ex.EvidenceFromClaim).AppendLine();
                sb.AppendLine();
                sb.AppendLine($"> {ex.Text}");
                sb.AppendLine();
            }
        }

        if (d.SoftSignals.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Signály na preverenie");
            sb.AppendLine();
            foreach (var signal in d.SoftSignals)
                sb.AppendLine($"- {signal}");
        }

        if (!string.IsNullOrEmpty(result.Summary))
        {
            sb.AppendLine();
            sb.AppendLine("## Zhrnutie");
            sb.AppendLine();
            sb.AppendLine(result.Summary);
        }

        return sb.ToString();
    }
}
