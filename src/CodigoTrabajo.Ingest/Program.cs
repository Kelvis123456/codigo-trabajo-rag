using System.Text.Encodings.Web;
using System.Text.Json;
using CodigoTrabajo.Core;

// parse  <texto.txt> <articles.json>   parte el texto de pdftotext en artículos
// embed  <articles.json> <embeddings.bin>   pide los embeddings a Gemini (necesita GEMINI_API_KEY)
var command = args.ElementAtOrDefault(0) ?? "parse";

switch (command)
{
    case "parse":
        Parse(args.ElementAtOrDefault(1) ?? "data/raw/codigo.txt", args.ElementAtOrDefault(2) ?? "data/articles.json");
        break;
    case "embed":
        await EmbedAsync(args.ElementAtOrDefault(1) ?? "data/articles.json", args.ElementAtOrDefault(2) ?? "data/embeddings.bin");
        break;
    default:
        Console.Error.WriteLine("Comandos: parse, embed");
        return 1;
}
return 0;

static void Parse(string input, string output)
{
    var articles = CodeParser.Parse(File.ReadAllText(input));
    var json = JsonSerializer.Serialize(articles, new JsonSerializerOptions
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    });

    var numbered = articles.Where(a => a.Number > 0).ToList();
    // Un artículo con un encabezado raro haría que todo lo siguiente se pegara al anterior sin avisar.
    if (!numbered.Select(a => a.Number).SequenceEqual(Enumerable.Range(1, 738)))
        throw new InvalidDataException($"Se esperaban los artículos 1 a 738 seguidos y salieron {numbered.Count}; no escribo {output}.");
    File.WriteAllText(output, json);
    Console.WriteLine($"{articles.Count - numbered.Count} principios, {numbered.Count} artículos " +
        $"(del {numbered[0].Number} al {numbered[^1].Number}), {articles.Sum(a => a.Notes.Count)} notas");
}

static async Task EmbedAsync(string input, string output)
{
    var key = Environment.GetEnvironmentVariable("GEMINI_API_KEY")
        ?? throw new InvalidOperationException("Falta GEMINI_API_KEY.");
    var articles = JsonSerializer.Deserialize<List<Article>>(File.ReadAllText(input))!;
    using var http = new HttpClient();
    var vectors = await new GeminiEmbedder(http, key).EmbedDocumentsAsync(articles.Select(DocumentText).ToList());
    VectorIndex.Save(vectors, output);
    Console.WriteLine($"{vectors.Length} vectores de {vectors[0].Length} dimensiones en {output}");
}

static string DocumentText(Article a)
{
    var text = $"{a.Label}. {a.Context}. {a.Text}";
    return text.Length <= 6000 ? text : text[..6000];
}
