namespace ClaimsIntake.Core.Policies;

public record PolicyChunk
{
    public required string ParagraphNumber { get; init; }

    public required int ArticleNumber { get; init; }

    public required string ArticleTitle { get; init; }

    public required string Text { get; init; }
}

public record ScoredChunk(PolicyChunk Chunk, float Score);
