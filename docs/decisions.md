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

## 2026-11-03 · Lo que dejó una revisión adversarial

Una revisión de solo lectura encontró fallos reales que arreglé:
- `/api/search` no tenía límite y, con clave, cada búsqueda embebe la pregunta: se podía agotar la cuota con peticiones GET en bucle, y con la cuota agotada cada petición esperaba unos 5 minutos por los reintentos. Ahora tiene su propio límite (30 por minuto), el embedding de una pregunta reintenta una sola vez y espera 2 segundos, y los cuerpos tienen un tope de 4 KB.
- La comprobación de citas se saltaba con `[Arts. 76 y 999]`, `[Art. 76, 999]`, `[Articulo 999]` sin tilde, `Art. 999` sin corchetes o corchetes de otro alfabeto, y `[Art. 99999999999]` o dígitos como `٩٩٩` hacían lanzar una excepción y devolvían un 500. Ahora se extrae cualquier mención de artículo, con o sin corchetes, y los dígitos de otros alfabetos se pasan a ASCII antes de analizar.
- La pregunta del usuario se pegaba sin delimitar y antes de los artículos, así que una pregunta con saltos de línea podía imitar una línea de artículo real. Ahora va al final, en una sola línea y bajo un encabezado que el sistema declara como texto no confiable.
- El comando `parse` no comprobaba que salieran los artículos 1 a 738 seguidos, y el CI no ejecutaba el test de paridad de JS cuando solo cambiaba `docs/`.
- Dependabot no cubría NuGet, el límite por IP en IPv6 daba un cupo por cada dirección de un /64, y los vectores se leían sin comprobar las dimensiones.

**No lo arreglé:**
- La comprobación de citas sigue mirando el número, no el contenido. Si el modelo cita `[Art. 76]` pero resume mal, o una pregunta lo convence de que diga algo falso citando un artículo recuperado, la cita sale como comprobada. Reducí el riesgo con el delimitado, no lo eliminé.
- El parser sigue dependiendo de la secuencia: un encabezado de artículo con un formato nuevo se pegaría al anterior. Ahora `parse` falla en voz alta en vez de escribir datos rotos, pero no sabe leer formatos que no conoce.
- Detrás de un proxy todos los usuarios compartirían un solo cupo, porque no hay `UseForwardedHeaders`. No está desplegado.
- La versión de JS y la de C# difieren con caracteres raros (`x²`, matemáticos en negrita, ligaduras) porque `char.IsLetterOrDigit` y `\p{L}\p{N}` no coinciden. El corpus no tiene ninguno, así que no cambia ningún resultado.
- La revisión la hizo un modelo de la misma familia que escribió el código, no una independiente. Sigue pendiente la de Codex.
