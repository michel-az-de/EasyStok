using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.UseCases.FeatureFlags;
using EasyStock.Application.UseCases.Pedidos.Cobranca;
using EasyStock.Domain.Entities.Pagamentos;
using EasyStock.Domain.Enums.Pagamentos;
using EasyStock.Domain.Sales;
using EasyStock.Domain.ValueObjects.Pix;

namespace EasyStock.Application.Tests.UseCases.Pedidos.Cobranca;

/// <summary>
/// #2233: Pix direto para a chave da loja, sem Mercado Pago. Atrás da flag <c>pagamento.pix_direto</c>, desligada por
/// padrão: sem ela ou sem chave cadastrada, nada muda. Ligada, o pedido entra na fila como o da entrega (decisão do
/// Felipe em 05/11) com o saldo a receber, e a baixa é sempre de uma pessoa. Só chaves fictícias.
/// </summary>
public class TrocarFormaPagamentoPixDiretoTests
{
    private const string ChaveFicticia = "loja@exemplo.test";
    private static readonly Guid Usuario = Guid.NewGuid();

    private static CobrancaPedidoFixture Cenario(bool flag = true, bool chave = true, CobrancaPedidoFixture? fixture = null)
    {
        var f = fixture ?? new CobrancaPedidoFixture();
        var loja = Loja.Criar(f.EmpresaId, "Loja Centro");
        if (chave)
            loja.DefinirRecebedorPix(RecebedorPix.Criar(ChavePix.Criar(TipoChavePix.Email, ChaveFicticia), "Casa de Doces", "São Paulo"));
        f.Pedido.LojaId = loja.Id;
        f.LojaRepo.GetByIdAsync(f.EmpresaId, loja.Id).Returns(loja);
        if (flag)
            f.Flags.ListarAtivasAsync(f.EmpresaId, Arg.Any<CancellationToken>()).Returns([FeatureCatalogo.PagamentoPixDireto]);
        return f;
    }

    private static TrocarFormaPagamentoPedidoInput PixDireto(CobrancaPedidoFixture f) =>
        new(f.EmpresaId, f.Pedido.Id, TrocarFormaPagamentoPedidoUseCase.FormaPixDireto, Usuario, "Operadora");

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public async Task SemFlagOuSemChave_Recusa_ENadaMuda(bool flag, bool chave)
    {
        var f = Cenario(flag, chave);

        var act = () => f.Trocar().ExecuteAsync(PixDireto(f));

        var erro = await act.Should().ThrowAsync<UseCaseValidationException>();
        erro.Which.Message.Should().NotContain(ChaveFicticia);
        f.Cobrancas.Should().BeEmpty();
        f.Eventos.Should().BeEmpty();
        f.Pedido.Status.Should().Be(StatusPedidoMapper.AguardandoPagamento);
        f.Preferencias.Should().BeEmpty();
    }

    [Fact]
    public async Task FlagDesligada_NaoConsultaALoja()
    {
        var f = Cenario(flag: false);

        await FluentActions.Awaiting(() => f.Trocar().ExecuteAsync(PixDireto(f))).Should().ThrowAsync<UseCaseValidationException>();

        await f.LojaRepo.DidNotReceiveWithAnyArgs().GetByIdAsync(default, default);
    }

    [Fact]
    public async Task ComFlagEChave_CriaCobrancaPixDireto_EColocaNaFila_SemMercadoPago()
    {
        var f = Cenario();

        var r = await f.Trocar().ExecuteAsync(PixDireto(f));

        var cobranca = f.Cobrancas.Should().ContainSingle().Subject;
        cobranca.Provedor.Should().Be(CobrancaPedido.ProvedorPixDireto);
        cobranca.Status.Should().Be(StatusCobrancaPedido.Pendente);
        cobranca.Valor.Should().Be(25m);
        cobranca.ExpiraEm.Should().BeNull("Pix direto não vence: nenhum job o expira");
        cobranca.PixCopiaCola.Should().StartWith("000201").And.Contain("540525.00").And.Contain("Casa de Doces")
            .And.Contain("Sao Paulo").And.Contain(BrCodePix.TxidDoPedido(f.Pedido.Id));
        r.Cobranca.Provedor.Should().Be(CobrancaPedido.ProvedorPixDireto);
        r.Cobranca.PixCopiaCola.Should().Be(cobranca.PixCopiaCola);
        r.Cobranca.LinkPagamento.Should().BeNull();
        f.Pedido.Status.Should().Be(StatusPedidoMapper.Aguardando, "decisão do Felipe em 05/11: entra na fila como o da entrega");
        f.Preferencias.Should().BeEmpty();
        f.MpClient.ReceivedCalls().Should().BeEmpty("Pix direto não chama o Mercado Pago");
        var evento = f.Eventos.Should().ContainSingle(e => e.Tipo == "forma_pagamento_trocada").Subject;
        evento.Detalhes.Should().Be("nenhuma -> pix_direto");
        evento.UsuarioId.Should().Be(Usuario);
    }

    [Fact]
    public async Task MesmaFormaPendente_Reutiliza()
    {
        var f = Cenario();
        await f.Trocar().ExecuteAsync(PixDireto(f));

        var r = await f.Trocar().ExecuteAsync(PixDireto(f));

        r.Cobranca.Reutilizada.Should().BeTrue();
        f.Cobrancas.Should().ContainSingle();
    }

    [Fact]
    public async Task EncomendaComSinal_CopiaEColaCobraOSinal_ESaldoFicaInteiro()
    {
        var f = new CobrancaPedidoFixture();
        f.Pedido.CriadoEm = CobrancaPedidoFixture.Agora;
        f.Pedido.AgendadoParaEm = CobrancaPedidoFixture.Agora.AddDays(3);
        Cenario(fixture: f);

        await f.Trocar().ExecuteAsync(PixDireto(f));

        var cobranca = f.Cobrancas.Should().ContainSingle().Subject;
        cobranca.Valor.Should().Be(25m, "a cobrança manual guarda o saldo, como a da entrega");
        cobranca.PixCopiaCola.Should().Contain("540512.50", "o Pix pede só o sinal");
        f.Pedido.Status.Should().Be(StatusPedidoMapper.AguardandoPagamento, "encomenda espera o sinal, como na entrega");
    }

    [Fact]
    public async Task MarcarPago_QuitaACobrancaPix_ERegistraQuemEQuando()
    {
        var f = Cenario();
        await f.Trocar().ExecuteAsync(PixDireto(f));

        await f.RegistrarManual().ExecuteAsync(new(f.EmpresaId, f.Pedido.Id, "pix", 25m,
            RegistradoPorUserId: Usuario, RegistradoPorNome: "Operadora"));

        var cobranca = f.Cobrancas.Should().ContainSingle().Subject;
        cobranca.Status.Should().Be(StatusCobrancaPedido.Paga);
        var pagamento = f.Pedido.Pagamentos.Should().ContainSingle().Subject;
        pagamento.Metodo.Should().Be("pix");
        pagamento.RegistradoPorUserId.Should().Be(Usuario);
        pagamento.RegistradoPorNome.Should().Be("Operadora");
        pagamento.PagoEm.Should().NotBe(default);
    }

    [Fact]
    public async Task IdaEVoltaPeloMercadoPago_DeixaUmaCobrancaPendente()
    {
        var f = Cenario();
        await f.Trocar().ExecuteAsync(PixDireto(f));
        await f.Trocar().ExecuteAsync(new(f.EmpresaId, f.Pedido.Id, "online", Usuario, "Operadora"));

        await f.Trocar().ExecuteAsync(PixDireto(f));

        var pendente = f.Cobrancas.Should().ContainSingle(c => c.Status == StatusCobrancaPedido.Pendente).Subject;
        pendente.Provedor.Should().Be(CobrancaPedido.ProvedorPixDireto);
    }

    [Fact]
    public async Task ComConversa_TentaMandarOPixNaConversa()
    {
        var f = Cenario();
        var conversa = Guid.NewGuid();
        f.Cobrancas.Add(CobrancaPedido.CriarNaEntrega(f.EmpresaId, f.Pedido.Id, 25m, CobrancaPedidoFixture.Agora.AddMinutes(-5), conversa));

        await f.Trocar().ExecuteAsync(PixDireto(f));

        await f.ConversaRepo.Received(1).ObterPorIdAsync(f.EmpresaId, conversa, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AChaveNaoVaiParaATrilhaNemParaOsEventos()
    {
        var f = Cenario();

        await f.Trocar().ExecuteAsync(PixDireto(f));

        f.Eventos.Should().OnlyContain(e => e.Detalhes == null || !e.Detalhes.Contains(ChaveFicticia));
        f.Publicador.ReceivedCalls().SelectMany(c => c.GetArguments())
            .Select(a => a?.ToString() ?? string.Empty).Should().NotContain(a => a.Contains(ChaveFicticia));
        f.OperacaoEventos.ReceivedCalls().SelectMany(c => c.GetArguments())
            .Select(a => a?.ToString() ?? string.Empty).Should().NotContain(a => a.Contains(ChaveFicticia));
    }

    [Fact]
    public void TextoAoCliente_TemValorCopiaEColaENomeDoRecebedor()
    {
        var texto = AvisoCobrancaConversa.TextoPixDireto(25m, 0m, "000201COPIA", "CASA DE DOCES");

        texto.Should().Contain("R$ 25,00").And.Contain("000201COPIA").And.Contain("CASA DE DOCES");
    }

    [Fact]
    public void TextoAoCliente_DaEncomenda_DizOSinalEORestante()
    {
        var texto = AvisoCobrancaConversa.TextoPixDireto(12.5m, 12.5m, "000201COPIA", "CASA DE DOCES");

        texto.Should().Contain("Sinal de R$ 12,50").And.Contain("Restante na entrega: R$ 12,50");
    }
}
