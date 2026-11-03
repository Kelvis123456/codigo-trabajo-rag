using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CodigoTrabajo.Tests;

/// <summary>La API sin clave de Gemini: es la configuración con la que corre el CI y la que debe degradar bien.</summary>
public class ApiTests : IClassFixture<ApiTests.NoKeyFactory>
{
    public sealed class NoKeyFactory : WebApplicationFactory<Program>
    {
        // Una clave vacía en la configuración gana sobre GEMINI_API_KEY del entorno, así el test no gasta cuota.
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder) =>
            builder.UseSetting("Gemini:ApiKey", "");
    }

    private readonly HttpClient _client;

    public ApiTests(NoKeyFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Health_responde_ok()
    {
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/health")).StatusCode);
    }

    [Fact]
    public async Task Un_articulo_existente_se_devuelve_con_su_texto()
    {
        var json = await _client.GetFromJsonAsync<JsonElement>("/api/articles/76");
        Assert.Equal(76, json.GetProperty("number").GetInt32());
        Assert.Contains("aviso previo", json.GetProperty("text").GetString());
    }

    [Theory]
    [InlineData("/api/articles/0")]
    [InlineData("/api/articles/739")]
    [InlineData("/api/articles/-3")]
    public async Task Un_articulo_que_no_existe_da_404(string url)
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task Sin_clave_la_busqueda_usa_palabras_y_encuentra_el_articulo()
    {
        var json = await _client.GetFromJsonAsync<JsonElement>("/api/search?q=salario%20de%20Navidad");
        Assert.Equal("palabras", json.GetProperty("mode").GetString());
        Assert.Contains(json.GetProperty("results").EnumerateArray(), r => r.GetProperty("number").GetInt32() == 219);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Una_busqueda_vacia_es_400(string q)
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync($"/api/search?q={q}")).StatusCode);
    }

    [Fact]
    public async Task Una_busqueda_demasiado_larga_es_400()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync($"/api/search?q={new string('a', 301)}")).StatusCode);
    }

    [Fact]
    public async Task Sin_clave_preguntar_responde_503_y_no_un_error_interno()
    {
        var res = await _client.PostAsJsonAsync("/api/ask", new { question = "¿Cuántos días de vacaciones?" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, res.StatusCode);
    }
}
