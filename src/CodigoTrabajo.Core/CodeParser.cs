using System.Text;
using System.Text.RegularExpressions;

namespace CodigoTrabajo.Core;

/// <summary>
/// Parte el texto que sale de <c>pdftotext -layout</c> en artículos. El PDF del Ministerio trae
/// encabezados de página, notas al pie mezcladas y números de nota pegados al del artículo
/// ("Art. 7288" es el 728 con la nota 8), así que el número de cada artículo se valida contra el
/// que toca en la secuencia en vez de fiarse del texto.
/// </summary>
public static partial class CodeParser
{
    [GeneratedRegex(@"^\s*PRINCIPIOS FUNDAMENTALES\s*$", RegexOptions.Multiline)]
    private static partial Regex StartRx();

    [GeneratedRegex(@"^\s*Art\.+\s?(\d+)\s*(?:[.\-–—]+|(?=\())\s*(.*)$")]
    private static partial Regex ArticleRx();

    [GeneratedRegex(@"^\s*PRINCIPIO\s+([IVXL]+)\s*$")]
    private static partial Regex PrincipleRx();

    [GeneratedRegex(@"^\s*(LIBRO|T[IÍ]TULO|CAP[IÍ]TULO|SECCI[OÓ]N)\s+[\wÁÉÍÓÚ]+\s*:?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex HeadingRx();

    [GeneratedRegex(@"^\s*(\d{1,2})\.-\s+\S")]
    private static partial Regex NoteStartRx();

    [GeneratedRegex(@"^\s*(Código de Trabajo de la República Dominicana\s+\d+|\d+\s+Secretaría de Estado de Trabajo)\s*$")]
    private static partial Regex FurnitureRx();

    private sealed class Builder(int number, string label, string context)
    {
        public int Number { get; } = number;
        public string Label { get; } = label;
        public string Context { get; } = context;
        public int Marker { get; init; }
        public List<string> Lines { get; } = [];
        public List<string> Notes { get; } = [];
    }

    public static IReadOnlyList<Article> Parse(string text, int lastArticle = 738)
    {
        var start = StartRx().Match(text);
        if (start.Success) text = text[start.Index..];

        var result = new List<Builder>();
        var headings = new Dictionary<int, string>();
        Builder? cur = null;
        var expected = 1;
        var done = false;

        foreach (var page in text.Split('\f'))
        {
            if (done) break;
            var lines = page.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => !FurnitureRx().IsMatch(l)).ToList();
            var noteAt = lines.FindIndex(l => NoteStartRx().IsMatch(l));
            var body = noteAt >= 0 ? lines[..noteAt] : lines;
            var onPage = new List<Builder>();

            for (var i = 0; i < body.Count && !done; i++)
            {
                var line = body[i];

                if (HeadingRx().IsMatch(line))
                {
                    var block = new List<string> { line.Trim().TrimEnd(':') };
                    while (i + 1 < body.Count && !string.IsNullOrWhiteSpace(body[i + 1])) block.Add(body[++i].Trim());
                    SetHeading(headings, block);
                    continue;
                }

                if (PrincipleRx().Match(line) is { Success: true } p)
                {
                    cur = new Builder(0, $"Principio {p.Groups[1].Value}", "Principios fundamentales");
                    result.Add(cur);
                    onPage.Add(cur);
                    continue;
                }

                if (ArticleRx().Match(line) is { Success: true } a && Resolve(a.Groups[1].Value, expected) is { } marker)
                {
                    cur = new Builder(expected, $"Artículo {expected}", Context(headings)) { Marker = marker };
                    result.Add(cur);
                    onPage.Add(cur);
                    cur.Lines.Add(a.Groups[2].Value);
                    expected++;
                    continue;
                }

                if (cur is null) continue;
                // El último artículo es de un solo párrafo; lo que sigue ya son los reglamentos.
                if (cur.Number == lastArticle && string.IsNullOrWhiteSpace(line) && cur.Lines.Count > 0)
                {
                    done = true;
                    break;
                }
                cur.Lines.Add(line);
            }

            if (noteAt >= 0) AttachNotes(lines[noteAt..], onPage, cur);
        }

        return result.Select(b => new Article(b.Number, b.Label, b.Context, Clean(b.Lines), b.Notes)).ToList();
    }

    /// <summary>Devuelve el número de nota pegado al del artículo (0 si no hay), o null si no es el artículo esperado.</summary>
    private static int? Resolve(string digits, int expected)
    {
        var want = expected.ToString();
        if (digits == want) return 0;
        if (digits.StartsWith(want, StringComparison.Ordinal) && digits.Length - want.Length <= 2)
            return int.Parse(digits[want.Length..]);
        return null;
    }

    private static void SetHeading(Dictionary<int, string> headings, List<string> block)
    {
        var kind = block[0].Split(' ')[0].ToUpperInvariant().Replace("Í", "I").Replace("Ó", "O");
        var rank = kind switch { "LIBRO" => 0, "TITULO" => 1, "CAPITULO" => 2, _ => 3 };
        foreach (var k in headings.Keys.Where(k => k >= rank).ToList()) headings.Remove(k);
        headings[rank] = string.Join(": ", block.Take(2));
    }

    private static string Context(Dictionary<int, string> headings) =>
        string.Join(" › ", headings.OrderBy(h => h.Key).Select(h => h.Value));

    private static void AttachNotes(List<string> noteLines, List<Builder> onPage, Builder? last)
    {
        string? text = null;
        var number = 0;
        var notes = new List<(int Number, string Text)>();
        foreach (var line in noteLines)
        {
            if (NoteStartRx().Match(line) is { Success: true } m)
            {
                if (text is not null) notes.Add((number, Clean([text])));
                number = int.Parse(m.Groups[1].Value);
                text = line.Trim()[(m.Groups[1].Value.Length + 2)..].Trim();
            }
            else if (text is not null) text += " " + line.Trim();
        }
        if (text is not null) notes.Add((number, Clean([text])));

        foreach (var (n, t) in notes)
        {
            var target = onPage.FirstOrDefault(b => b.Marker == n) ?? onPage.LastOrDefault() ?? last;
            target?.Notes.Add(t);
        }
    }

    private static string Clean(List<string> lines)
    {
        var sb = new StringBuilder();
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                if (sb.Length > 0 && sb[^1] != '\n') sb.Append('\n');
                continue;
            }
            if (sb.Length > 1 && sb[^1] == '-' && char.IsLetter(sb[^2]) && char.IsLower(line[0]))
                sb.Length--; // palabra partida al final de la línea
            else if (sb.Length > 0 && sb[^1] != '\n')
                sb.Append(' ');
            sb.Append(line);
        }
        return Regex.Replace(sb.ToString().Trim(), @"[ \t]+", " ");
    }
}
