using Azure;
using Azure.AI.DocumentIntelligence;
using ClaimsIntake.Core.Models;

namespace ClaimsIntake.Core
{
    public class AzureDocumentIntelligence
    {
        private readonly DocumentIntelligenceClient _client;

        public AzureDocumentIntelligence(string endpoint, string apiKey)
        {
            _client = new DocumentIntelligenceClient(new Uri(endpoint), new AzureKeyCredential(apiKey));
        }

        public async Task<DocumentAnalysisResult> AnalyzeAsync(byte[] fileBytes, CancellationToken ct = default)
        {
            Operation<AnalyzeResult> operation = await _client.AnalyzeDocumentAsync(
                WaitUntil.Completed,          // wait for completion (long-running operation)
                "prebuilt-layout",           // model: extracts text, tables and structure
                BinaryData.FromBytes(fileBytes),
                cancellationToken: ct);

            AnalyzeResult result = operation.Value;

            return new DocumentAnalysisResult
            {
                FullText = result.Content
            };
        }
    }
}
