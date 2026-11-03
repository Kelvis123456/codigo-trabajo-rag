# Decisiones

Cada entrada dice qué elegí, qué descarté y por qué.

## 2026-11-03 · Parsear el PDF del Ministerio y no un sitio de leyes

**Elegí:** el PDF oficial de transparencia.mt.gob.do, convertido con `pdftotext -layout`.

**Descarté:** copias de despachos de abogados y de Scribd. Varias son consolidaciones viejas y no sé qué reformas incluyen. El PDF oficial también tiene problemas (ver abajo), pero al menos sé de dónde viene.

**Lo que tuvo el PDF:**
- Encabezados de página dentro del texto y palabras partidas con guion al final de línea.
- Notas al pie mezcladas con el cuerpo. Un número de nota queda pegado al del artículo: "Art. 7288" es el 728 con la nota 8.
- Cuatro formas de escribir el encabezado de un artículo: `Art. 94.-`, `Art. 94.` (sin guion), `Art. 184 (Modificado por la Ley...)` y `Art.. 361-`.
- Después del artículo 738 el PDF sigue con reglamentos que reinician la numeración en 1.

**Cómo lo resolví:** el número de cada artículo se valida contra el que toca en la secuencia. Si no es el esperado ni el esperado con una nota pegada, la línea se trata como texto. Así los reglamentos no abren artículos falsos. Descubrí las cuatro variantes de encabezado porque el parser se paraba en el 94, luego en el 183 y luego en el 360: cada vez que faltaba un artículo, todo lo siguiente se iba al artículo anterior. Ahora un test comprueba que salen los 738, seguidos, ninguno vacío.

## 2026-11-03 · Vectores primero, BM25 de respaldo, sin fusionar

**Elegí:** buscar por embeddings de Gemini y caer a BM25 solo cuando no hay vectores o clave.

**Descarté:** fusionar las dos listas con Reciprocal Rank Fusion, que es lo que suele recomendarse. Lo probé con mis 21 preguntas: el top 5 pasó de 21/21 (solo vectores) a 20/21, y el artículo correcto dejó de ir primero en 6 casos más (18 contra 12). Mi lectura, sin haberla probado aparte: BM25 sube artículos del mismo tema que repiten las palabras de la pregunta, y la fusión los deja por delante del correcto.

**Sobre la medida:** son 21 preguntas que escribí yo, leyendo el texto para saber la respuesta esperada. No ajusté nada mirando el resultado de los vectores, pero tampoco es un conjunto grande. Sirve para comparar métodos y detectar regresiones. El test falla si BM25 baja de 14/21 o los vectores de 20/21.

**Lo que BM25 no resuelve:** "¿cuál es la jornada máxima?" devuelve la garantía de salarios del artículo 465 antes que el 147, porque la palabra "máxima" pesa más que "ocho horas por día". Con vectores sale el 147 segundo, y los fallos que quedan están a un puesto del primero.

## 2026-11-03 · Comprobar las citas, no las frases

**Elegí:** que el servicio de respuesta compare cada `[Art. N]` del texto del modelo con los artículos que se le entregaron. Los que no estén se devuelven aparte como `unverifiedCitations` y la página los marca.

**Descarté:** pedirle al modelo una salida JSON con las fuentes. Un modelo puede llenar ese campo con números inventados igual que el texto. Comprobar contra la lista real que entregué es lo único que no depende de que el modelo se porte bien.

**Lo que esto no cubre:** que el modelo cite el artículo correcto pero resuma mal lo que dice. Eso necesita leer la respuesta contra el artículo; hoy no lo mido. Está en el README como límite.

## 2026-11-03 · IChatClient y embeddings por REST

El modelo va por `IChatClient` de Microsoft.Extensions.AI, igual que en AgendaBot, para poder cambiar de proveedor sin tocar el servicio de respuesta; los tests usan un modelo falso con respuestas escritas a mano. Los embeddings van por REST directo en vez de por el SDK: solo necesito dos llamadas y así quedan a la vista el tipo de tarea (documento o consulta) y las 768 dimensiones. Con dimensiones reducidas Gemini no normaliza el vector, así que lo normalizo yo y el coseno es un producto punto.

## 2026-11-03 · Los vectores van en el repo

`data/embeddings.bin` pesa 2.3 MB y se genera con la clave de quien lo corra. Lo subí para que clonar y ejecutar no cueste cuota ni exija clave para la búsqueda. Si cambia el parser, hay que regenerarlo; `VectorIndex.Load` falla con un mensaje claro si el número de vectores no coincide con el de artículos.
