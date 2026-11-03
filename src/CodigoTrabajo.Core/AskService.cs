using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace CodigoTrabajo.Core;

public sealed record Answer(
    string Text,
    IReadOnlyList<Article> Cited,
    IReadOnlyList<int> UnverifiedCitations,
    IReadOnlyList<Article> Retrieved);

/// <summary>
/// Recupera los artículos, se los da al modelo y comprueba que cada artículo que cite haya sido uno de los
/// que se le dieron. Un número que el modelo se inventó o recordó de memoria no se presenta como fuente.
/// </summary>
public sealed partial class AskService(Retriever retriever, IChatClient chat)
{
    public const string System = """
        Respondes preguntas sobre el Código de Trabajo de la República Dominicana (Ley 16-92).
        Usa SOLO los artículos que te doy debajo de la pregunta. Si no alcanzan para responder, di que no
        los encuentras en el texto y no completes con lo que sepas de memoria.
        Cita cada afirmación con el número entre corchetes, así: [Art. 76]. Cita solo artículos de la lista.
        Responde en español, en pocas frases. No des asesoría legal ni digas qué debe hacer la persona:
        explica qué dice el texto.
        """;

    [GeneratedRegex(@"\[\s*Art(?:ículo|\.)?\s*(\d+)\s*\]", RegexOptions.IgnoreCase)]
    private static partial Regex CitationRx();

    public async Task<Answer> AskAsync(string question, int top = 5, CancellationToken ct = default)
    {
        var hits = await retriever.SearchAsync(question, top, ct);
        var retrieved = hits.Select(h => h.Article).ToList();

        var prompt = new StringBuilder().AppendLine($"Pregunta: {question}").AppendLine().AppendLine("Artículos:");
        foreach (var a in retrieved)
            prompt.AppendLine($"[{(a.Number > 0 ? $"Art. {a.Number}" : a.Label)}] {a.Text}");

        var response = await chat.GetResponseAsync(
            [new ChatMessage(ChatRole.System, System), new ChatMessage(ChatRole.User, prompt.ToString())],
            new ChatOptions { Temperature = 0.1f, MaxOutputTokens = 700 }, ct);

        var text = response.Text.Trim();
        var numbers = CitationRx().Matches(text).Select(m => int.Parse(m.Groups[1].Value)).Distinct().ToList();
        var byNumber = retrieved.Where(a => a.Number > 0).ToDictionary(a => a.Number);
        var cited = numbers.Where(byNumber.ContainsKey).Select(n => byNumber[n]).ToList();
        var unverified = numbers.Where(n => !byNumber.ContainsKey(n)).ToList();
        return new Answer(text, cited, unverified, retrieved);
    }
}
