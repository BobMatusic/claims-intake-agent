namespace ClaimsIntake.Core.Models
{
    public class DocumentAnalysisResult
    {
        public string FullText { get; init; } = string.Empty;

        public Dictionary<string, string> Fields { get; init; } = [];
    }
}
