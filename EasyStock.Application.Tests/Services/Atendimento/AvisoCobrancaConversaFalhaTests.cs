using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Application.Tests.Services.Atendimento;

/// <summary>
/// #1724: o aviso de cobrança que o canal não entregava só virava log. O link reemitido não chegava, o pedido
/// era cancelado 30 minutos depois e ninguém via que o cliente nunca recebeu nada.
/// </summary>
public class AvisoCobrancaConversaFalhaTests
{
    private static readonly DateTime Agora = new(2026, 10, 14, 15, 0, 0, DateTimeKind.Utc);
    private readonly Guid _empresa = Guid.NewGuid();
    private readonly IConversaRepository _conversas = Substitute.For<IConversaRepository>();
    private readonly ICanalMensageria _canal = Substitute.For<ICanalMensageria>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly List<Mensagem> _gravadas = [];

    private (AvisoCobrancaConversa Aviso, Conversa Conversa) Montar(DateTime ultimaEntrada)
    {
        _canal.Canal.Returns(CanalConversa.WhatsApp);
        var conversa = Conversa.Abrir(_empresa, "5511987654321", ultimaEntrada, "Maria");
        conversa.RegistrarEntrada(ultimaEntrada);
        _conversas.ObterPorIdAsync(_empresa, conversa.Id, Arg.Any<CancellationToken>()).Returns(conversa);
        _conversas.When(r => r.AddMensagemAsync(Arg.Any<Mensagem>(), Arg.Any<CancellationToken>())).Do(ci => _gravadas.Add(ci.Arg<Mensagem>()));
        return (new AvisoCobrancaConversa(_conversas, new ResolvedorCanal([_canal]), _uow, NullLogger<AvisoCobrancaConversa>.Instance), conversa);
    }

    [Fact]
    public async Task Canal_fora_grava_o_aviso_como_mensagem_com_falha()
    {
        var (aviso, conversa) = Montar(Agora.AddMinutes(-10));
        _canal.EnviarTextoAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new HttpRequestException("429 limite de taxa"));

        var enviado = await aviso.EnviarAsync(_empresa, conversa.Id, AvisoCobrancaConversa.TextoNovoLink("https://mp.test/x"), Agora);

        enviado.Should().BeFalse();
        var falhou = _gravadas.Should().ContainSingle().Subject;
        falhou.Status.Should().Be(StatusMensagem.Falhou);
        falhou.Autor.Should().Be(AutorMensagem.Sistema);
        falhou.Texto.Should().Contain("https://mp.test/x");
        await _uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task Fora_da_janela_de_24_horas_grava_a_falha_sem_chamar_o_canal()
    {
        var (aviso, conversa) = Montar(Agora.AddHours(-30));

        var enviado = await aviso.EnviarAsync(_empresa, conversa.Id, AvisoCobrancaConversa.TextoPedidoCancelado, Agora);

        enviado.Should().BeFalse();
        _gravadas.Should().ContainSingle().Which.Status.Should().Be(StatusMensagem.Falhou);
        await _canal.DidNotReceive().EnviarTextoAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Canal_que_entrega_grava_a_mensagem_enviada()
    {
        var (aviso, conversa) = Montar(Agora.AddMinutes(-10));
        _canal.EnviarTextoAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("wamid.1");

        (await aviso.EnviarAsync(_empresa, conversa.Id, AvisoCobrancaConversa.TextoPedidoCancelado, Agora)).Should().BeTrue();

        _gravadas.Should().ContainSingle().Which.Status.Should().Be(StatusMensagem.Enviada);
    }
}