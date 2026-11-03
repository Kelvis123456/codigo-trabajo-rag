using System.Text.Encodings.Web;
using System.Text.Json;
using CodigoTrabajo.Core;

// Uso: dotnet run --project src/CodigoTrabajo.Ingest -- data/raw/codigo.txt data/articles.json
var input = args.ElementAtOrDefault(0) ?? "data/raw/codigo.txt";
var output = args.ElementAtOrDefault(1) ?? "data/articles.json";

var articles = CodeParser.Parse(File.ReadAllText(input));
var json = JsonSerializer.Serialize(articles, new JsonSerializerOptions
{
    WriteIndented = true,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
});
File.WriteAllText(output, json);

var numbered = articles.Where(a => a.Number > 0).ToList();
Console.WriteLine($"{articles.Count - numbered.Count} principios, {numbered.Count} artículos " +
    $"(del {numbered[0].Number} al {numbered[^1].Number}), {articles.Sum(a => a.Notes.Count)} notas");
