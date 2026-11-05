Español: [README.es.md](README.es.md)

# codigo-trabajo-rag

A web page where you ask questions about the Dominican Labor Code (Ley 16-92) in your own words and get an answer that cites the articles it came from. If the answer cites an article the system never gave the model, the page flags it.

I built it to learn how to put together a RAG system end to end in .NET with something I can check: a closed text of 738 articles and questions whose answers I can read myself.

**Stack:** .NET 10 · ASP.NET Core minimal APIs · Microsoft.Extensions.AI · Gemini (embeddings and model) · xUnit · GitHub Actions

## How it works

1. `CodeParser` splits the text of the Ministry of Labor's PDF into articles and footnotes.
2. Each article is turned into a vector with `gemini-embedding-001` (768 dimensions). The vectors are in `data/embeddings.bin`, so you do not have to regenerate them to run the project.
3. When you ask a question, the question is embedded and the 5 closest articles are taken.
4. The model receives only those 5 articles and the instruction to cite every claim as `[Art. 76]`.
5. Each cited number is checked against the 5 articles it was given. Anything not among them is shown as "unverified".

Without `GEMINI_API_KEY` the page still starts, search falls back to BM25 (keywords) and `/api/ask` returns 503.

## How well it finds the articles

I wrote 21 questions in `data/eval.json` and read in the text which article answers each one. I measure whether that article shows up in the top 5:

| Method | First | In the top 5 | MRR |
|---|---|---|---|
| BM25 (keywords) | 11 of 21 | 14 of 21 | 0.58 |
| Gemini vectors | 18 of 21 | 21 of 21 | 0.93 |
| BM25 + vectors (RRF) | 12 of 21 | 20 of 21 | 0.70 |

I kept vectors only, with BM25 as a fallback. The fusion made the result worse; I think it is because BM25 puts articles on the same topic that repeat the question's words ahead of the right one, but I did not test that separately. It is in [docs/decisions.md](docs/decisions.md).

This measures search, not whether the final answer is correct. It is 21 questions written by me, so it is useful for spotting regressions and comparing methods, not for promising an accuracy rate on other people's questions.

## How to run it

You need .NET 10. The key is optional.

```bash
export GEMINI_API_KEY=...        # sin esto solo hay búsqueda por palabras
dotnet run --project src/CodigoTrabajo.Api
```

Open the URL it prints and ask a question. The tests do not need a key:

```bash
dotnet test
```

The vector test against Gemini skips itself if there is no key. If there is one, it uses about 21 embedding calls.

### Regenerating the data

The PDF is not in the repo. Download it from the Ministry of Labor and convert it to text:

```bash
curl -L -o data/raw/codigo.pdf https://transparencia.mt.gob.do/images/docs/publicaciones/codigo-de-trabajo.pdf
pdftotext -enc UTF-8 -layout data/raw/codigo.pdf data/raw/codigo.txt
dotnet run --project src/CodigoTrabajo.Ingest -- parse
dotnet run --project src/CodigoTrabajo.Ingest -- embed
```

`pdftotext` comes with poppler or xpdf.

## What it does not do

- It is not legal advice. It explains what the text says; it does not tell you what to do in your case.
- The text is the one in the Ministry's PDF, which includes the Code with its amendment notes as far as the PDF includes them. I do not know whether it reflects later reforms. Before relying on an article, compare it with the current version.
- The citation check only verifies that the number is among the articles that were provided. It does not verify that the model's sentence says the same thing as the article.
- It does not include the regulations or the ILO conventions that come in the same PDF. Only the Code and its fundamental principles.
- It is not deployed. A public deployment would need a spending cap on the Gemini account, in addition to the per-IP limit that `/api/ask` already has.

## Structure

```
src/CodigoTrabajo.Core     parser, BM25, vectores, servicio de respuesta
src/CodigoTrabajo.Ingest   comandos parse y embed
src/CodigoTrabajo.Api      API y página
tests/                     parser, búsqueda, citas y API
data/                      artículos, vectores y preguntas de evaluación
```
