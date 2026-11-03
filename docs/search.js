// BM25 sobre los artículos, el mismo algoritmo de src/CodigoTrabajo.Core/Bm25Index.cs.
// Está duplicado a propósito para que la demo estática no necesite servidor ni clave;
// tests/js/search.test.mjs comprueba que da las mismas listas que la versión de C#.

const K1 = 1.4;
const B = 0.75;

const STOP = new Set(
  ("a al ante bajo con contra cual cuando de del desde donde el ella en entre es esa ese esta este ha han hay " +
    "la las le les lo los mas me mi no o para pero por que se si sin sobre su sus te tiene tienen un una uno y ya " +
    "como cuanto cuantos cuales puede pueden debe deben ser son fue sera seran estos").split(" "),
);
const SUFFIXES = ["aciones", "iciones", "acion", "icion", "mente", "ando", "iendo", "adores", "ador", "ados", "adas", "idos", "idas"];

function stem(w) {
  for (const s of SUFFIXES) if (w.length > s.length + 3 && w.endsWith(s)) return w.slice(0, -s.length);
  if (w.length > 4 && w.endsWith("es")) w = w.slice(0, -2);
  else if (w.length > 3 && w.endsWith("s")) w = w.slice(0, -1);
  if (w.length > 4 && "aeo".includes(w[w.length - 1])) w = w.slice(0, -1);
  return w;
}

export function tokens(text) {
  const plain = text.normalize("NFD").replace(/\p{M}/gu, "").toLowerCase();
  return plain
    .split(/[^\p{L}\p{N}]+/u)
    .filter((w) => w.length >= 2 && !STOP.has(w))
    .map(stem);
}

export class Bm25 {
  constructor(articles) {
    this.articles = articles;
    this.postings = new Map();
    this.lengths = articles.map((a, d) => {
      const toks = tokens(`${a.Context} ${a.Text}`);
      const tf = new Map();
      for (const t of toks) tf.set(t, (tf.get(t) ?? 0) + 1);
      for (const [t, n] of tf) {
        if (!this.postings.has(t)) this.postings.set(t, []);
        this.postings.get(t).push([d, n]);
      }
      return toks.length;
    });
    this.avg = this.lengths.reduce((a, b) => a + b, 0) / (this.lengths.length || 1);
  }

  search(query, top = 5) {
    const scores = new Map();
    for (const term of new Set(tokens(query))) {
      const list = this.postings.get(term);
      if (!list) continue;
      const idf = Math.log(1 + (this.articles.length - list.length + 0.5) / (list.length + 0.5));
      for (const [doc, tf] of list) {
        const norm = (tf * (K1 + 1)) / (tf + K1 * (1 - B + (B * this.lengths[doc]) / this.avg));
        scores.set(doc, (scores.get(doc) ?? 0) + idf * norm);
      }
    }
    return [...scores.entries()]
      .sort((a, b) => b[1] - a[1] || a[0] - b[0])
      .slice(0, top)
      .map(([d, score]) => ({ article: this.articles[d], score }));
  }
}
