using System.Text.Json;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento.Ferramentas;
using EasyStock.Domain.Entities.Atendimento;

namespace EasyStock.Application.Tests.Services.Atendimento.Ferramentas;

/// <summary>S54: <c>consultar_caderno</c> devolve o texto dos trechos pelo código do índice, só da empresa da conversa.</summary>
public class ConsultarCadernoFerramentaTests
{
    private static readonly DateTime Agora = new(2026, 10, 1, 15, 0, 0, DateTimeKind.Utc);

    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly ICadernoRepository _repo = Substitute.For<ICadernoRepository>();
    private readonly TrechoCaderno _troca;

    public ConsultarCadernoFerramentaTests()
    {
        _troca = TrechoCaderno.Criar(_empresaId, "Troca", "Trocamos em até 24 h.", "troca", nucleo: false, Agora);
        _repo.ListarAsync(_empresaId, false, Arg.Any<CancellationToken>()).Returns([_troca]);
    }

    private ContextoTurnoAgente Contexto() =>
        new(_empresaId, Conversa.Abrir(_empresaId, "5511999998888", Agora, "Maria"), Agora);

    private static JsonElement Entrada(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public async Task DevolveTextoPeloCodigo()
    {
        var resposta = await new ConsultarCadernoFerramenta(_repo)
            .ExecutarAsync(Contexto(), Entrada($$"""{"codigos":["{{_troca.Codigo.ToUpperInvariant()}}"]}"""));

        using var json = JsonDocument.Parse(resposta);
        var trecho = json.RootElement.GetProperty("trechos")[0];
        trecho.GetProperty("codigo").GetString().Should().Be(_troca.Codigo);
        trecho.GetProperty("texto").GetString().Should().Be("Trocamos em até 24 h.");
        json.RootElement.TryGetProperty("naoEncontrados", out _).Should().BeFalse();
    }

    [Fact]
    public async Task CodigoDesconhecidoVaiParaNaoEncontrados()
    {
        var resposta = await new ConsultarCadernoFerramenta(_repo)
            .ExecutarAsync(Contexto(), Entrada($$"""{"codigos":["{{_troca.Codigo}}","zzzzzzzz"]}"""));

        using var json = JsonDocument.Parse(resposta);
        json.RootElement.GetProperty("trechos").GetArrayLength().Should().Be(1);
        json.RootElement.GetProperty("naoEncontrados")[0].GetString().Should().Be("zzzzzzzz");
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"codigos":[]}""")]
    public async Task SemCodigoDevolveErro(string entrada)
    {
        var resposta = await new ConsultarCadernoFerramenta(_repo).ExecutarAsync(Contexto(), Entrada(entrada));

        resposta.Should().Contain("codigos_obrigatorios");
    }

    [Fact]
    public async Task LeSoAEmpresaDaConversa()
    {
        await new ConsultarCadernoFerramenta(_repo)
            .ExecutarAsync(Contexto(), Entrada($$"""{"codigos":["{{_troca.Codigo}}"]}"""));

        await _repo.Received(1).ListarAsync(_empresaId, false, Arg.Any<CancellationToken>());
    }
}
