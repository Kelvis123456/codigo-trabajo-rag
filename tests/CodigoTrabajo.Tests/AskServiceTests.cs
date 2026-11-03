using CodigoTrabajo.Core;
using Microsoft.Extensions.AI;

namespace CodigoTrabajo.Tests;

public class AskServiceTests
{
    private static readonly Article[] Corpus =
    [
        new(76, "Artículo 76", "Libro I", "La parte que ejerce el desahucio debe dar aviso previo de siete días después de tres meses de trabajo.", []),
        new(177, "Artículo 177", "Libro III", "Los empleadores deben conceder catorce días laborables de vacaciones después de un año de trabajo.", []),
        new(233, "Artículo 233", "Libro IV", "La mujer no puede ser despedida por el hecho de estar embarazada.", []),
    ];

    private sealed class FakeChat(string reply) : IChatClient
    {
        public List<ChatMessage> Seen { get; } = [];

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default)
        {
            Seen.AddRange(messages);
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, reply)));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default) => throw new NotSupportedException();
        public object? GetService(Type serviceType, object? key = null) => null;
        public void Dispose() { }
    }

    private static AskService Service(FakeChat chat) => new(new Retriever(new Bm25Index(Corpus), null, null), chat);

    [Fact]
    public async Task Entrega_al_modelo_los_articulos_recuperados_y_no_los_demas()
    {
        var chat = new FakeChat("Dice catorce días [Art. 177].");
        await Service(chat).AskAsync("¿Cuántos días de vacaciones tengo?");

        var user = chat.Seen.Last().Text;
        Assert.Contains("[Art. 177]", user);
        Assert.DoesNotContain("[Art. 233]", user);
    }

    [Fact]
    public async Task Una_cita_de_un_articulo_recuperado_cuenta_como_fuente()
    {
        var answer = await Service(new FakeChat("Catorce días laborables [Art. 177].")).AskAsync("vacaciones después de un año");

        Assert.Equal([177], answer.Cited.Select(a => a.Number));
        Assert.Empty(answer.UnverifiedCitations);
    }

    [Fact]
    public async Task Una_cita_que_no_estaba_entre_los_articulos_dados_se_marca_sin_verificar()
    {
        var answer = await Service(new FakeChat("Catorce días [Art. 177] y además [Art. 999].")).AskAsync("vacaciones después de un año");

        Assert.Equal([177], answer.Cited.Select(a => a.Number));
        Assert.Equal([999], answer.UnverifiedCitations);
    }

    [Fact]
    public async Task Una_respuesta_sin_citas_no_inventa_fuentes()
    {
        var answer = await Service(new FakeChat("No lo encuentro en el texto.")).AskAsync("vacaciones después de un año");

        Assert.Empty(answer.Cited);
        Assert.Empty(answer.UnverifiedCitations);
        Assert.NotEmpty(answer.Retrieved);
    }

    [Fact]
    public async Task Acepta_las_variantes_de_formato_de_la_cita()
    {
        var answer = await Service(new FakeChat("Ver [Artículo 76] y [art. 177].")).AskAsync("desahucio vacaciones aviso");

        Assert.Equal([76, 177], answer.Cited.Select(a => a.Number).OrderBy(n => n));
    }
}
