using System.Text.Json;
using CodigoTrabajo.Core;
using Xunit.Abstractions;

namespace CodigoTrabajo.Tests;

/// <summary>
/// Mide la recuperación con las preguntas de data/eval.json (la respuesta esperada de cada una la leí a mano
/// en el texto del Código). La de BM25 corre siempre; la de vectores llama a Gemini y solo corre si hay GEMINI_API_KEY.
/// </summary>
public class RetrievalEvalTests(ITestOutputHelper output)
{
    private sealed record Case(string Question, int[] Expected);

    private static readonly string Root = FindRoot();
    private static readonly List<Article> All = JsonSerializer.Deserialize<List<Article>>(File.ReadAllText(Path.Combine(Root, "data", "articles.json")))!;
    private static readonly List<Case> Cases = JsonSerializer.Deserialize<List<Case>>(
        File.ReadAllText(Path.Combine(Root, "data", "eval.json")), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    [Fact]
    public void BM25_encuentra_el_articulo_en_los_cinco_primeros_en_al_menos_14_de_21()
    {
        var index = new Bm25Index(All);
        var hits = Cases.Count(c => index.Search(c.Question, 5).Any(h => c.Expected.Contains(h.Article.Number)));
        output.WriteLine($"BM25 hit@5 = {hits}/{Cases.Count}");
        Assert.True(hits >= 14, $"BM25 bajó a {hits}/{Cases.Count}");
    }

    [Fact]
    public async Task Vectores_de_Gemini_en_los_cinco_primeros_en_al_menos_20_de_21()
    {
        var key = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        if (key is null) return; // sin clave no hay cómo embeber las preguntas

        var vectors = VectorIndex.Load(All, Path.Combine(Root, "data", "embeddings.bin"));
        using var http = new HttpClient();
        var embedder = new GeminiEmbedder(http, key);
        int hit1 = 0, hit5 = 0;
        double mrr = 0;
        foreach (var c in Cases)
        {
            var ranked = vectors.Search(await embedder.EmbedQueryAsync(c.Question), 5).Select(h => h.Article.Number).ToList();
            var rank = ranked.FindIndex(c.Expected.Contains);
            if (rank >= 0) { hit5++; mrr += 1.0 / (rank + 1); if (rank == 0) hit1++; }
            else output.WriteLine($"MISS exp=[{string.Join(",", c.Expected)}] got=[{string.Join(",", ranked)}] {c.Question}");
            await Task.Delay(700);
        }
        output.WriteLine($"vectores: hit@1={hit1}/{Cases.Count} hit@5={hit5}/{Cases.Count} MRR={mrr / Cases.Count:F2}");
        Assert.True(hit5 >= 20, $"vectores bajó a {hit5}/{Cases.Count}");
    }

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "data", "articles.json"))) dir = dir.Parent;
        return dir?.FullName ?? throw new FileNotFoundException("data/articles.json");
    }
}
