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

        // sends a document (bytes from Blazor upload) to Azure and returns extracted text + fields
        public async Task<DocumentAnalysisResult> AnalyzeAsync(byte[] fileBytes, CancellationToken ct = default)
        {
            Operation<AnalyzeResult> operation = await _client.AnalyzeDocumentAsync(
                WaitUntil.Completed,          // wait for completion (long-running operation)
                "prebuilt-layout",           // model: extracts text, tables and structure
                BinaryData.FromBytes(fileBytes),
                cancellationToken: ct);

            AnalyzeResult result = operation.Value;

            // full document text (main output – the agent extracts data from this)
            string fullText = result.Content;

            // key-value pairs (e.g. "Contract number: SK123"); may be empty with the layout model
            var fields = new Dictionary<string, string>();
            if (result.KeyValuePairs is not null)
            {
                foreach (var kv in result.KeyValuePairs)
                {
                    var key = kv.Key?.Content;
                    var value = kv.Value?.Content;
                    if (!string.IsNullOrWhiteSpace(key) && value is not null)
                        fields[key] = value;
                }
            }

            return new DocumentAnalysisResult
            {
                FullText = result.Content,
                Fields = fields
            };
        }
    }
}
