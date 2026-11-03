using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace CodigoTrabajo.Core;

public interface IEmbedder
{
    Task<float[][]> EmbedDocumentsAsync(IReadOnlyList<string> texts, CancellationToken ct = default);
    Task<float[]> EmbedQueryAsync(string text, CancellationToken ct = default);
}

/// <summary>
/// Embeddings de Gemini por REST. Uso la API directa y no el SDK porque solo necesito dos llamadas
/// y así el cuerpo de la petición (tipo de tarea, dimensiones) queda a la vista.
/// </summary>
public sealed class GeminiEmbedder(HttpClient http, string apiKey, string model = "gemini-embedding-001") : IEmbedder
{
    public const int Dimensions = 768;
    private const int BatchSize = 100;

    public async Task<float[][]> EmbedDocumentsAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        var all = new List<float[]>();
        foreach (var chunk in texts.Chunk(BatchSize))
        {
            var body = new JsonObject
            {
                ["requests"] = new JsonArray(chunk.Select(t => (JsonNode)Request(t, "RETRIEVAL_DOCUMENT")).ToArray()),
            };
            var json = await PostAsync($"models/{model}:batchEmbedContents", body, maxRetries: 6, ct);
            all.AddRange(json["embeddings"]!.AsArray().Select(e => Normalize(e!["values"]!.AsArray().Select(v => (float)v!).ToArray())));
        }
        return [.. all];
    }

    public async Task<float[]> EmbedQueryAsync(string text, CancellationToken ct = default)
    {
        // Una pregunta de un usuario no espera minutos: con un solo reintento corto, si la cuota se acabó se falla rápido.
        var json = await PostAsync($"models/{model}:embedContent", Request(text, "RETRIEVAL_QUERY"), maxRetries: 1, ct);
        return Normalize(json["embedding"]!["values"]!.AsArray().Select(v => (float)v!).ToArray());
    }

    private JsonObject Request(string text, string task) => new()
    {
        ["model"] = $"models/{model}",
        ["content"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = text }) },
        ["taskType"] = task,
        ["outputDimensionality"] = Dimensions,
    };

    private async Task<JsonNode> PostAsync(string path, JsonObject body, int maxRetries, CancellationToken ct)
    {
        // El plan gratis corta por minuto: se espera y se reintenta en vez de fallar a la mitad de la indexación.
        for (var attempt = 0; ; attempt++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"https://generativelanguage.googleapis.com/v1beta/{path}")
            {
                Content = JsonContent.Create(body),
            };
            req.Headers.Add("x-goog-api-key", apiKey);
            using var res = await http.SendAsync(req, ct);
            if (res.StatusCode == HttpStatusCode.TooManyRequests && attempt < maxRetries)
            {
                await Task.Delay(TimeSpan.FromSeconds(maxRetries > 1 ? 15 * (attempt + 1) : 2), ct);
                continue;
            }
            res.EnsureSuccessStatusCode();
            return (await res.Content.ReadFromJsonAsync<JsonNode>(ct))!;
        }
    }

    // Con dimensiones reducidas Gemini no devuelve el vector normalizado; con norma 1 el coseno es un producto punto.
    internal static float[] Normalize(float[] v)
    {
        var norm = MathF.Sqrt(v.Sum(x => x * x));
        return norm == 0 ? v : v.Select(x => x / norm).ToArray();
    }
}
