using System.Numerics.Tensors;
using Microsoft.Extensions.AI;

namespace ClaimsIntake.Core.Policies;

public class PolicyConditionsIndexer
{
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;

    private IReadOnlyList<PolicyChunk> _chunks = [];
    private float[][] _vectors = [];

    public PolicyConditionsIndexer(IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator)
    {
        _embeddingGenerator = embeddingGenerator;
    }

    public bool IsIndexed => _chunks.Count > 0;

    public async Task IndexAsync(
        IReadOnlyList<PolicyChunk> chunks,
        CancellationToken cancellationToken = default)
    {
        var inputs = chunks
            .Select(c => $"Článok {c.ArticleNumber} — {c.ArticleTitle}\n{c.Text}")
            .ToList();

        var embeddings = await _embeddingGenerator.GenerateAsync(inputs, cancellationToken: cancellationToken);

        _vectors = embeddings.Select(e => e.Vector.ToArray()).ToArray();
        _chunks = chunks;
    }

    public PolicyChunk? GetChunk(string paragraphNumber)
        => _chunks.FirstOrDefault(c => c.ParagraphNumber == paragraphNumber);

    public async Task<IReadOnlyList<PolicyChunk>> SearchAsync(
        string query,
        int topK = 5,
        int? articleNumber = null,
        CancellationToken cancellationToken = default)
    {
        if (_chunks.Count == 0)
            return [];

        var queryEmbedding = await _embeddingGenerator.GenerateAsync(query, cancellationToken: cancellationToken);
        var queryVector = queryEmbedding.Vector.ToArray();

        var scored = _vectors
            .Select((vector, index) => (Index: index, Score: TensorPrimitives.CosineSimilarity(queryVector, vector)));

        if (articleNumber.HasValue)
            scored = scored.Where(x => _chunks[x.Index].ArticleNumber == articleNumber.Value);

        return scored
            .OrderByDescending(x => x.Score)
            .Take(topK)
            .Select(x => _chunks[x.Index])
            .ToList();
    }
}
