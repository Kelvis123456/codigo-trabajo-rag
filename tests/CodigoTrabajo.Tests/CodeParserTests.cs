using CodigoTrabajo.Core;

namespace CodigoTrabajo.Tests;

public class CodeParserTests
{
    // Recorte con las rarezas reales del PDF: encabezado de página, palabra partida, número de nota
    // pegado al del artículo, "Art. 3." sin guion, "(Modificado ...)" en vez de guion y nota al pie.
    private const string Sample = """
        Texto previo que no es del Código

                          PRINCIPIOS FUNDAMENTALES

        PRINCIPIO I
            El trabajo es una función social.

                                      LIBRO PRIMERO:
                           DEL CONTRATO DE TRABAJO

                                        TITULO I:
                           Definición y sujetos del contrato

            Art. 1.- El contrato de trabajo es aquel por el cual una persona
        se obliga a prestar un servi-
        cio personal a otra.

            Art. 2.- Trabajador es toda persona física.
        Código de Trabajo de la República Dominicana  25

            Empleador es la persona a quien es prestado el servicio.

            Art. 33.- Se entiende por empresa la unidad económica.

        8.- Donde dice "empresa", debe leerse "unidad".
              Segunda línea de la nota.
        \f
            Art. 4. Los contratos especiales tienen un régimen propio.

            Art. 5 (Modificado por la Ley 25-98, del 15 de enero de 1998).
        El derecho a vacaciones no se pierde.

            Art. 6.- Último artículo de la prueba.
        Sigue en la segunda línea.

        Esto ya es de otro reglamento y no debe entrar.
        """;

    private static IReadOnlyList<Article> Parse() => CodeParser.Parse(Sample.Replace("\\f", "\f"), lastArticle: 6);

    [Fact]
    public void Ignora_lo_anterior_a_los_principios_y_los_cuenta_aparte()
    {
        var all = Parse();
        Assert.Equal("Principio I", all[0].Label);
        Assert.Equal(0, all[0].Number);
        Assert.Equal(6, all.Count(a => a.Number > 0));
    }

    [Fact]
    public void Une_las_palabras_partidas_al_final_de_la_linea()
    {
        var art = Parse().Single(a => a.Number == 1);
        Assert.Equal("El contrato de trabajo es aquel por el cual una persona se obliga a prestar un servicio personal a otra.", art.Text);
    }

    [Fact]
    public void Quita_el_encabezado_de_pagina_y_deja_el_resto_del_articulo()
    {
        var art = Parse().Single(a => a.Number == 2);
        Assert.DoesNotContain("República Dominicana  25", art.Text);
        Assert.Contains("Empleador es la persona", art.Text);
    }

    [Fact]
    public void Lee_un_numero_con_la_nota_pegada_como_el_articulo_que_toca()
    {
        var art = Parse().Single(a => a.Number == 3);
        Assert.StartsWith("Se entiende por empresa", art.Text);
    }

    [Fact]
    public void Adjunta_la_nota_al_pie_al_articulo_de_su_pagina_y_la_saca_del_texto()
    {
        var art = Parse().Single(a => a.Number == 3);
        Assert.Equal(["Donde dice \"empresa\", debe leerse \"unidad\". Segunda línea de la nota."], art.Notes);
        Assert.DoesNotContain("Segunda línea", art.Text);
    }

    [Fact]
    public void Acepta_punto_en_vez_de_guion_y_la_variante_modificado()
    {
        var all = Parse();
        Assert.StartsWith("Los contratos especiales", all.Single(a => a.Number == 4).Text);
        Assert.StartsWith("(Modificado por la Ley 25-98", all.Single(a => a.Number == 5).Text);
    }

    [Fact]
    public void Guarda_el_libro_y_el_titulo_como_contexto()
    {
        var art = Parse().Single(a => a.Number == 1);
        Assert.Contains("LIBRO PRIMERO", art.Context);
        Assert.Contains("Definición y sujetos del contrato", art.Context);
    }

    [Fact]
    public void Corta_despues_del_ultimo_articulo()
    {
        var art = Parse().Single(a => a.Number == 6);
        Assert.Equal("Último artículo de la prueba. Sigue en la segunda línea.", art.Text);
    }

    [Fact]
    public void Una_numeracion_que_no_sigue_la_secuencia_no_abre_un_articulo_nuevo()
    {
        var text = "PRINCIPIOS FUNDAMENTALES\n\n    Art. 1.- Uno.\n    Art. 40.- Esto es de un reglamento.\n";
        var all = CodeParser.Parse(text, lastArticle: 738);
        Assert.Single(all);
        Assert.Contains("Esto es de un reglamento", all[0].Text);
    }

    [Fact]
    public void El_texto_real_tiene_738_articulos_seguidos_y_ninguno_vacio()
    {
        var path = FindData();
        var all = System.Text.Json.JsonSerializer.Deserialize<List<Article>>(File.ReadAllText(path))!;
        var numbered = all.Where(a => a.Number > 0).ToList();
        Assert.Equal(Enumerable.Range(1, 738), numbered.Select(a => a.Number));
        Assert.All(numbered, a => Assert.True(a.Text.Length > 30, $"Artículo {a.Number} casi vacío"));
        Assert.DoesNotContain(numbered, a => a.Text.Contains('\f'));
    }

    [Fact]
    public void Articulos_conocidos_dicen_lo_que_dice_el_Codigo()
    {
        var all = System.Text.Json.JsonSerializer.Deserialize<List<Article>>(File.ReadAllText(FindData()))!;
        string Text(int n) => all.Single(a => a.Number == n).Text;
        Assert.StartsWith("El contrato de trabajo es aquel por el cual una persona", Text(1));
        Assert.Contains("aviso previo", Text(76));
        Assert.Contains("vacaciones", Text(177));
        Assert.StartsWith("La reglamentación de la garantía", Text(738));
    }

    private static string FindData()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "data", "articles.json"))) dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new FileNotFoundException("data/articles.json"), "data", "articles.json");
    }
}
