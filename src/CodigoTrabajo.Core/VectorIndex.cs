namespace CodigoTrabajo.Core;

/// <summary>Índice vectorial en memoria. 738 vectores de 768 floats: una búsqueda lineal tarda microsegundos.</summary>
public sealed class VectorIndex(IReadOnlyList<Article> articles, float[][] vectors)
{
    public static VectorIndex Load(IReadOnlyList<Article> articles, string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path));
        var count = reader.ReadInt32();
        var dim = reader.ReadInt32();
        if (count != articles.Count) throw new InvalidDataException($"{path} tiene {count} vectores y hay {articles.Count} artículos; vuelve a generarlo.");
        var vectors = new float[count][];
        for (var i = 0; i < count; i++)
        {
            vectors[i] = new float[dim];
            for (var j = 0; j < dim; j++) vectors[i][j] = reader.ReadSingle();
        }
        return new VectorIndex(articles, vectors);
    }

    public static void Save(float[][] vectors, string path)
    {
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write(vectors.Length);
        writer.Write(vectors[0].Length);
        foreach (var v in vectors)
            foreach (var x in v) writer.Write(x);
    }

    public IReadOnlyList<Hit> Search(float[] query, int top = 5) =>
        vectors.Select((v, i) => (i, score: Dot(v, query)))
            .OrderByDescending(s => s.score).Take(top)
            .Select(s => new Hit(articles[s.i], s.score)).ToList();

    private static double Dot(float[] a, float[] b)
    {
        double sum = 0;
        for (var i = 0; i < a.Length; i++) sum += a[i] * b[i];
        return sum;
    }
}

/// <summary>
/// Con vectores busca por significado; sin ellos (o sin clave para embeber la pregunta) cae a BM25.
/// Probé también fusionar las dos listas (RRF) y empeoraba: ver docs/decisions.md.
/// </summary>
public sealed class Retriever(Bm25Index lexical, VectorIndex? vectors, IEmbedder? embedder)
{
    public bool UsesVectors => vectors is not null && embedder is not null;

    public async Task<IReadOnlyList<Hit>> SearchAsync(string query, int top = 5, CancellationToken ct = default) =>
        UsesVectors
            ? vectors!.Search(await embedder!.EmbedQueryAsync(query, ct), top)
            : lexical.Search(query, top);
}
