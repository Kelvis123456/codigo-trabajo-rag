import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { Bm25 } from "../../docs/search.js";

const read = (p) => JSON.parse(readFileSync(new URL(p, import.meta.url), "utf8"));
const articles = read("../../docs/articles.json");
const cases = read("../../data/eval.json");

// Top 5 que devuelve Bm25Index.cs para cada pregunta de data/eval.json (en el mismo orden), copiados de una corrida real.
const FROM_CSHARP = [
  [177, 80, 76, 178, 704], [219, 221, 220, 222, 195], [75, 78, 79, 95, 85], [81, 702, 85, 301, 32],
  [90, 94, 87, 100, 93], [91, 93, 95, 94, 233], [233, 235, 242, 0, 232], [465, 152, 149, 148, 146],
  [203, 158, 157, 204, 162], [233, 90, 18, 231, 241], [100, 101, 99, 98, 102], [165, 164, 163, 159, 156],
  [223, 560, 194, 379, 425], [25, 34, 297, 26, 35], [324, 386, 319, 354, 372], [412, 406, 411, 658, 401],
  [192, 239, 260, 263, 653], [702, 196, 233, 73, 95], [54, 272, 242, 55, 212], [213, 214, 217, 216, 721],
  [52, 725, 726, 727, 728],
];

const index = new Bm25(articles);

test("la copia del corpus en docs/ es idéntica a data/", () => {
  assert.deepEqual(articles, read("../../data/articles.json"));
});

test("hay una lista de referencia por cada pregunta de evaluación", () => {
  assert.equal(FROM_CSHARP.length, cases.length);
});

test("da el mismo top 5, en el mismo orden, que la versión de C#", () => {
  cases.forEach((c, i) => {
    const got = index.search(c.question, 5).map((h) => h.article.Number);
    assert.deepEqual(got, FROM_CSHARP[i], c.question);
  });
});

test("encuentra el artículo esperado en el top 5 de al menos 14 de 21 preguntas", () => {
  const hits = cases.filter((c) => index.search(c.question, 5).some((h) => c.expected.includes(h.article.Number)));
  assert.ok(hits.length >= 14, `solo ${hits.length}`);
});

test("una consulta sin palabras útiles no devuelve nada y no falla", () => {
  assert.deepEqual(index.search("   ", 5), []);
  assert.deepEqual(index.search("el de la", 5), []);
});
