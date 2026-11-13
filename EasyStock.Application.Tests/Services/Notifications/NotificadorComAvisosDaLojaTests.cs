using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Services.Notifications;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Application.Tests.Services.Notifications;

/// <summary>#2096: o interruptor da tela Avisos barra o evento antes do motor, só quando o payload traz o código.</summary>
public class NotificadorComAvisosDaLojaTests
{
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly INotificadorService _interno = Substitute.For<INotificadorService>();
    private readonly IConfiguracaoAtendimentoRepository _configuracoes = Substitute.For<IConfiguracaoAtendimentoRepository>();

    private NotificadorComAvisosDaLoja Criar(AvisosOperacao avisos, bool comRepositorio = true)
    {
        _configuracoes.ObterAvisosAsync(_empresaId, Arg.Any<CancellationToken>()).Returns(avisos.Validado());
        var servicos = new ServiceCollection();
        if (comRepositorio) servicos.AddSingleton(_configuracoes);
        return new NotificadorComAvisosDaLoja(_interno, servicos.BuildServiceProvider(), NullLogger<NotificadorComAvisosDaLoja>.Instance);
    }

    private static string Prazo(TipoPrazo tipo) =>
        PrazoEstouradoEvento.Payload(tipo, Guid.NewGuid(), "ABC12345", "10:00", "20 minutos");

    [Fact]
    public async Task AvisoDesligadoNaoChegaAoMotor()
    {
        var sut = Criar(new AvisosOperacao { PedidoAtrasado = false });

        var id = await sut.EnfileirarEventoAsync(TipoEventoNotificacao.PrazoEstourado, _empresaId, Prazo(TipoPrazo.PedidoAtrasado));

        id.Should().Be(Guid.Empty);
        await _interno.DidNotReceiveWithAnyArgs().EnfileirarEventoAsync(default, default, default!);
    }

    [Fact]
    public async Task AvisoLigadoPassaComOsMesmosArgumentos()
    {
        var sut = Criar(new AvisosOperacao { PedidoAtrasado = false });
        var payload = Prazo(TipoPrazo.ClienteSemResposta);
        var referencia = Guid.NewGuid();

        await sut.EnfileirarEventoAsync(TipoEventoNotificacao.PrazoEstourado, _empresaId, payload, referencia, default, "prazo:x");

        await _interno.Received(1).EnfileirarEventoAsync(TipoEventoNotificacao.PrazoEstourado, _empresaId, payload,
            referencia, Arg.Any<CancellationToken>(), "prazo:x");
    }

    [Fact]
    public async Task TempoZeroDesligaOProntoSemEntregador()
    {
        var sut = Criar(new AvisosOperacao { ProntoSemEntregadorMinutos = 0 });

        await sut.EnfileirarEventoAsync(TipoEventoNotificacao.PrazoEstourado, _empresaId, Prazo(TipoPrazo.ProntoSemEntregador));

        await _interno.DidNotReceiveWithAnyArgs().EnfileirarEventoAsync(default, default, default!);
    }

    [Fact]
    public async Task AlertaDaIaDesligadoNaoChegaAoMotor()
    {
        var sut = Criar(new AvisosOperacao { ReclamacaoCliente = false });
        var payload = AlertaOperacaoEvento.Payload(CodigosAviso.ReclamacaoCliente, "t", "r", "x", "l", "av:rc:1");

        await sut.EnfileirarEventoAsync(TipoEventoNotificacao.AlertaOperacao, _empresaId, payload);

        await _interno.DidNotReceiveWithAnyArgs().EnfileirarEventoAsync(default, default, default!);
    }

    public static TheoryData<string> AvisosDaEquipeDesligaveis() => new()
    {
        CodigosAviso.EquipeAtencao, CodigosAviso.EquipePedido, CodigosAviso.EquipePagamento,
    };

    [Theory]
    [MemberData(nameof(AvisosDaEquipeDesligaveis))]
    public async Task AvisoDaEquipeDesligadoNaoChegaAoMotor(string codigo)
    {
        // #2211: os três avisos da equipe usam o mesmo interruptor da tela Avisos.
        var sut = Criar(new AvisosOperacao
        {
            EquipeAtencao = codigo != CodigosAviso.EquipeAtencao,
            EquipePedido = codigo != CodigosAviso.EquipePedido,
            EquipePagamento = codigo != CodigosAviso.EquipePagamento,
        });
        var publicador = new PublicadorAvisoEquipe(sut);
        var pedido = Guid.NewGuid();

        await (codigo switch
        {
            CodigosAviso.EquipeAtencao => publicador.AtencaoNoAtendimentoAsync(_empresaId, pedido, "Marina", "pediu uma pessoa"),
            CodigosAviso.EquipePedido => publicador.PedidoComCanhotoAsync(_empresaId, pedido, "Marina", 10m),
            _ => publicador.ProblemaNoPagamentoAsync(_empresaId, pedido, "Marina", "recusado", "p1"),
        });

        await _interno.DidNotReceiveWithAnyArgs().EnfileirarEventoAsync(default, default, default!);
    }

    [Fact]
    public async Task AvisoDaEquipeLigadoPorPadraoChegaAoMotor()
    {
        var sut = Criar(AvisosOperacao.Padrao);

        await new PublicadorAvisoEquipe(sut).PedidoComCanhotoAsync(_empresaId, Guid.NewGuid(), "Marina", 10m);

        await _interno.Received(1).EnfileirarEventoAsync(TipoEventoNotificacao.AvisoEquipePedidoComCanhoto, _empresaId,
            Arg.Is<string>(p => p.Contains("\"aviso\":\"equipe_pedido\"")), Arg.Any<Guid?>(), Arg.Any<CancellationToken>(),
            Arg.Any<string?>());
    }

    [Theory]
    [InlineData("{\"tipo_legivel\":\"Disparo de teste\"}")]
    [InlineData("não é json")]
    [InlineData("")]
    public async Task PayloadSemCodigoNaoConsultaALoja(string payload)
    {
        var sut = Criar(new AvisosOperacao { PedidoAtrasado = false });

        await sut.EnfileirarEventoAsync(TipoEventoNotificacao.PrazoEstourado, _empresaId, payload);

        await _interno.Received(1).EnfileirarEventoAsync(TipoEventoNotificacao.PrazoEstourado, _empresaId, payload,
            null, Arg.Any<CancellationToken>(), null);
        await _configuracoes.DidNotReceiveWithAnyArgs().ObterAvisosAsync(default);
    }

    [Fact]
    public async Task OutroTipoDeEventoNaoConsultaALoja()
    {
        var sut = Criar(new AvisosOperacao { PedidoAtrasado = false });

        await sut.EnfileirarEventoAsync(TipoEventoNotificacao.ConversaEscalada, _empresaId, Prazo(TipoPrazo.PedidoAtrasado));

        await _interno.ReceivedWithAnyArgs(1).EnfileirarEventoAsync(default, default, default!);
        await _configuracoes.DidNotReceiveWithAnyArgs().ObterAvisosAsync(default);
    }

    [Fact]
    public async Task HostSemAtendimentoRegistradoDeixaPassar()
    {
        // O Worker usa o notificador para avaliar eventos e não registra o repositório do atendimento.
        var sut = Criar(new AvisosOperacao { PedidoAtrasado = false }, comRepositorio: false);

        await sut.EnfileirarEventoAsync(TipoEventoNotificacao.PrazoEstourado, _empresaId, Prazo(TipoPrazo.PedidoAtrasado));

        await _interno.ReceivedWithAnyArgs(1).EnfileirarEventoAsync(default, default, default!);
    }
}
