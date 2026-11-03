using System.Text.Json;
using System.Threading.RateLimiting;
using CodigoTrabajo.Core;
using Microsoft.Extensions.AI;

var builder = WebApplication.CreateBuilder(args);

var dataDir = Path.Combine(AppContext.BaseDirectory, "data");
var articles = JsonSerializer.Deserialize<List<Article>>(File.ReadAllText(Path.Combine(dataDir, "articles.json")))!;
var lexical = new Bm25Index(articles);

// Con GEMINI_API_KEY (o Gemini:ApiKey) hay búsqueda por significado y respuestas; sin ella la app arranca
// igual y solo ofrece la búsqueda por palabras y la lectura de artículos.
var geminiKey = builder.Configuration["Gemini:ApiKey"] ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY");
IEmbedder? embedder = null;
VectorIndex? vectors = null;
IChatClient? chat = null;
if (!string.IsNullOrWhiteSpace(geminiKey))
{
    embedder = new GeminiEmbedder(new HttpClient { Timeout = TimeSpan.FromSeconds(20) }, geminiKey);
    vectors = VectorIndex.Load(articles, Path.Combine(dataDir, "embeddings.bin"));
    chat = new Google.GenAI.Client(apiKey: geminiKey).AsIChatClient(builder.Configuration["Agent:Model"] ?? "gemini-flash-lite-latest");
}
var retriever = new Retriever(lexical, vectors, embedder);

// /api/ask cuesta cuota del modelo en cada llamada, así que se limita por IP.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("ask", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "?",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = builder.Configuration.GetValue("RateLimit:AskPerMinute", 6), Window = TimeSpan.FromMinutes(1) }));
});

var app = builder.Build();
app.UseRateLimiter();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

var api = app.MapGroup("/api");

api.MapGet("/articles/{number:int}", (int number) =>
    articles.FirstOrDefault(a => a.Number == number && number > 0) is { } a ? Results.Ok(a) : Results.NotFound());

api.MapGet("/search", async (string? q, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(q) || q.Length > 300) return Results.BadRequest(new { error = "La búsqueda debe tener entre 1 y 300 caracteres." });
    var hits = await retriever.SearchAsync(q, 5, ct);
    return Results.Ok(new { mode = retriever.UsesVectors ? "vectores" : "palabras", results = hits.Select(h => Summary(h.Article)) });
});

if (chat is not null)
{
    var ask = new AskService(retriever, chat);
    api.MapPost("/ask", async (AskRequest body, CancellationToken ct) =>
    {
        if (string.IsNullOrWhiteSpace(body.Question) || body.Question.Length > 300)
            return Results.BadRequest(new { error = "La pregunta debe tener entre 1 y 300 caracteres." });
        try
        {
            var answer = await ask.AskAsync(body.Question, ct: ct);
            return Results.Ok(new
            {
                answer = answer.Text,
                cited = answer.Cited.Select(Summary),
                unverifiedCitations = answer.UnverifiedCitations,
                retrieved = answer.Retrieved.Select(Summary),
            });
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            // Cuota o caída del proveedor: no se devuelve el mensaje del SDK, que puede traer detalles internos.
            app.Logger.LogWarning(e, "Falló la llamada al modelo");
            return Results.Json(new { error = "El modelo no respondió. Prueba de nuevo en un minuto." }, statusCode: 502);
        }
    }).RequireRateLimiting("ask");
}
else
{
    api.MapPost("/ask", () => Results.Json(new { error = "Las respuestas están desactivadas: falta la clave del modelo en este despliegue." }, statusCode: 503));
}

app.Run();

static object Summary(Article a) => new { a.Number, a.Label, a.Context, a.Text, a.Notes };

record AskRequest(string? Question);

public partial class Program;
