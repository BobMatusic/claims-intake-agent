namespace ClaimsIntake.Core.Models;

public enum DocumentType
{
    ClaimReport,
    Invoice,
    Photo
}

public class UploadedDocument
{
    public string FileName { get; init; } = string.Empty;
    public byte[] Content { get; init; } = [];
    public DocumentType Type { get; set; }
    public string? ExtractedText { get; set; }
}
