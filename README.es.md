English: [README.md](README.md)

# codigo-trabajo-rag

Una página donde preguntas sobre el Código de Trabajo dominicano (Ley 16-92) con tus propias palabras y recibes una respuesta que cita los artículos de los que salió. Si la respuesta cita un artículo que el sistema no le dio al modelo, la página lo avisa.

Lo hice para aprender a armar un RAG de punta a punta en .NET con algo que se pueda comprobar: un texto cerrado de 738 artículos y preguntas cuya respuesta puedo leer yo mismo.

**Stack:** .NET 10 · ASP.NET Core minimal APIs · Microsoft.Extensions.AI · Gemini (embeddings y modelo) · xUnit · GitHub Actions

## Cómo funciona

1. `CodeParser` parte el texto del PDF del Ministerio de Trabajo en artículos y notas al pie.
2. Cada artículo se convierte en un vector con `gemini-embedding-001` (768 dimensiones). Los vectores están en `data/embeddings.bin`, así que no hay que regenerarlos para correr el proyecto.
3. Al preguntar, se embebe la pregunta y se toman los 5 artículos más cercanos.
4. El modelo recibe solo esos 5 artículos y la instrucción de citar cada afirmación como `[Art. 76]`.
5. Se comprueba que cada número citado esté entre los 5 que se le dieron. Lo que no esté se muestra como "sin verificar".

Sin `GEMINI_API_KEY` la página arranca igual, la búsqueda cae a BM25 (palabras) y `/api/ask` responde 503.

## Qué tan bien encuentra los artículos

Escribí 21 preguntas en `data/eval.json` y leí en el texto qué artículo contesta cada una. Mido si ese artículo aparece entre los 5 primeros:

| Método | Primero | En el top 5 | MRR |
|---|---|---|---|
| BM25 (palabras) | 11 de 21 | 14 de 21 | 0.58 |
| Vectores de Gemini | 18 de 21 | 21 de 21 | 0.93 |
| BM25 + vectores (RRF) | 12 de 21 | 20 de 21 | 0.70 |

Dejé solo vectores, con BM25 de respaldo. La fusión empeoró el resultado; creo que porque BM25 pone por delante artículos del mismo tema que repiten las palabras de la pregunta, pero no lo comprobé aparte. Está en [docs/decisions.md](docs/decisions.md).

Esto mide la búsqueda, no si la respuesta final es correcta. Son 21 preguntas escritas por mí, así que sirve para ver regresiones y comparar métodos, no para prometer un porcentaje de acierto en preguntas de otra gente.

## Cómo correrlo

Necesitas .NET 10. La clave es opcional.

```bash
export GEMINI_API_KEY=...        # sin esto solo hay búsqueda por palabras
dotnet run --project src/CodigoTrabajo.Api
```

Abre la URL que imprime y pregunta. Los tests no necesitan clave:

```bash
dotnet test
```

La prueba de vectores contra Gemini se salta sola si no hay clave. Si la hay, gasta unas 21 llamadas de embeddings.

### Regenerar los datos

El PDF no está en el repo. Bájalo del Ministerio de Trabajo y conviértelo a texto:

```bash
curl -L -o data/raw/codigo.pdf https://transparencia.mt.gob.do/images/docs/publicaciones/codigo-de-trabajo.pdf
pdftotext -enc UTF-8 -layout data/raw/codigo.pdf data/raw/codigo.txt
dotnet run --project src/CodigoTrabajo.Ingest -- parse
dotnet run --project src/CodigoTrabajo.Ingest -- embed
```

`pdftotext` viene con poppler o xpdf.

## Lo que no hace

- No es asesoría legal. Explica qué dice el texto; no te dice qué hacer en tu caso.
- El texto es el del PDF del Ministerio, que trae el Código con sus notas de modificaciones hasta donde el PDF las incluye. No sé si refleja reformas posteriores. Antes de apoyarte en un artículo, compáralo con la versión vigente.
- La comprobación de citas solo verifica que el número esté entre los artículos entregados. No verifica que la frase del modelo diga lo mismo que el artículo.
- No incluye los reglamentos ni los convenios de la OIT que vienen en el mismo PDF. Solo el Código y sus principios fundamentales.
- No está desplegado. Un despliegue público necesitaría un tope de gasto en la cuenta de Gemini, además del límite por IP que ya trae `/api/ask`.

## Estructura

```
src/CodigoTrabajo.Core     parser, BM25, vectores, servicio de respuesta
src/CodigoTrabajo.Ingest   comandos parse y embed
src/CodigoTrabajo.Api      API y página
tests/                     parser, búsqueda, citas y API
data/                      artículos, vectores y preguntas de evaluación
```
