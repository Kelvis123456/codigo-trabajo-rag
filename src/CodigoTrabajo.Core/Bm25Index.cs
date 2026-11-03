using System.Globalization;
using System.Text;

namespace CodigoTrabajo.Core;

public sealed record Hit(Article Article, double Score);

/// <summary>
/// Búsqueda léxica BM25 sobre los artículos. Sin dependencias ni servicio externo: el Código entero
/// son unos 700 KB, cabe en memoria y se indexa en milisegundos.
/// </summary>
public sealed class Bm25Index
{
    private const double K1 = 1.4;
    private const double B = 0.75;

    private readonly IReadOnlyList<Article> _articles;
    private readonly Dictionary<string, List<(int Doc, int Tf)>> _postings = [];
    private readonly int[] _lengths;
    private readonly double _avgLength;

    public Bm25Index(IReadOnlyList<Article> articles)
    {
        _articles = articles;
        _lengths = new int[articles.Count];
        for (var d = 0; d < articles.Count; d++)
        {
            // El contexto (libro y título) pesa poco pero ayuda con preguntas generales como "vacaciones".
            var tokens = SpanishText.Tokens($"{articles[d].Context} {articles[d].Text}");
            _lengths[d] = tokens.Count;
            foreach (var group in tokens.GroupBy(t => t))
            {
                if (!_postings.TryGetValue(group.Key, out var list)) _postings[group.Key] = list = [];
                list.Add((d, group.Count()));
            }
        }
        _avgLength = _lengths.Length == 0 ? 0 : _lengths.Average();
    }

    public IReadOnlyList<Hit> Search(string query, int top = 5)
    {
        var scores = new Dictionary<int, double>();
        foreach (var term in SpanishText.Tokens(query).Distinct())
        {
            if (!_postings.TryGetValue(term, out var list)) continue;
            var idf = Math.Log(1 + (_articles.Count - list.Count + 0.5) / (list.Count + 0.5));
            foreach (var (doc, tf) in list)
            {
                var norm = tf * (K1 + 1) / (tf + K1 * (1 - B + B * _lengths[doc] / _avgLength));
                scores[doc] = scores.GetValueOrDefault(doc) + idf * norm;
            }
        }
        return scores.OrderByDescending(s => s.Value).ThenBy(s => s.Key).Take(top)
            .Select(s => new Hit(_articles[s.Key], s.Value)).ToList();
    }
}

public static class SpanishText
{
    private static readonly HashSet<string> Stop = new(
    [
        "a", "al", "ante", "bajo", "con", "contra", "cual", "cuando", "de", "del", "desde", "donde", "el", "ella",
        "en", "entre", "es", "esa", "ese", "esta", "este", "ha", "han", "hay", "la", "las", "le", "les", "lo",
        "los", "mas", "me", "mi", "no", "o", "para", "pero", "por", "que", "se", "si", "sin", "sobre", "su",
        "sus", "te", "tiene", "tienen", "un", "una", "uno", "y", "ya", "como", "cuanto", "cuantos", "cual",
        "cuales", "puede", "pueden", "debe", "deben", "ser", "son", "fue", "sera", "seran", "este", "estos",
    ]);

    public static List<string> Tokens(string text)
    {
        var tokens = new List<string>();
        var sb = new StringBuilder();
        foreach (var ch in RemoveAccents(text).ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch)) { sb.Append(ch); continue; }
            Flush(sb, tokens);
        }
        Flush(sb, tokens);
        return tokens;
    }

    private static void Flush(StringBuilder sb, List<string> tokens)
    {
        if (sb.Length == 0) return;
        var word = sb.ToString();
        sb.Clear();
        if (word.Length < 2 || Stop.Contains(word)) return;
        tokens.Add(Stem(word));
    }

    // Raíz muy tosca: quita plural, "-mente" y las terminaciones de nombre más comunes. Con 738 artículos
    // basta para que "despidos", "despido" y "despedir" caigan cerca; un stemmer completo sería exceso.
    private static string Stem(string w)
    {
        foreach (var suffix in new[] { "aciones", "iciones", "acion", "icion", "mente", "ando", "iendo", "adores", "ador", "ados", "adas", "idos", "idas" })
            if (w.Length > suffix.Length + 3 && w.EndsWith(suffix, StringComparison.Ordinal)) return w[..^suffix.Length];
        if (w.Length > 4 && w.EndsWith("es", StringComparison.Ordinal)) w = w[..^2];
        else if (w.Length > 3 && w.EndsWith('s')) w = w[..^1];
        if (w.Length > 4 && "aeo".Contains(w[^1])) w = w[..^1];
        return w;
    }

    private static string RemoveAccents(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(ch);
        return sb.ToString();
    }
}
