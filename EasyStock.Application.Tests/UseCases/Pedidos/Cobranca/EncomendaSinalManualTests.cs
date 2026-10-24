using System.Text.Json;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Services.Pedidos;
using EasyStock.Application.Tests.Helpers;
using EasyStock.Application.UseCases.Pedidos.Cobranca;
using EasyStock.Application.UseCases.RegistrarPagamentoPedido;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Domain.Entities.Pagamentos;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Domain.Exceptions.Storefront;
using EasyStock.Domain.Sales;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute.ExceptionExtensions;

namespace EasyStock.Application.Tests.UseCases.Pedidos.Cobranca;

public class EncomendaSinalManualTests
{
    [Theory]
    [InlineData(0, 120, true)]
    [InlineData(30, 90, true)]
    [InlineData(0, 120, false)]
    [InlineData(30, 90, false)]
    public async Task EditarTotalManual_RecriaDividaAtualSemAlterarRecebimentos(decimal recebido, decimal saldoEsperado, bool cobrarSaldo)
    {
        var f = Futuro();
        var item = f.Pedido.Itens.Last();
        item.PrecoUnitario = 80m; item.RecalcularSubtotal(); f.Pedido.RecalcularTotal();
        var conversaId = Guid.NewGuid();
        var anterior = CobrancaPedido.CriarNaEntrega(f.EmpresaId, f.Pedido.Id, 100m, CobrancaPedidoFixture.Agora, conversaId);
        f.Cobrancas.Add(anterior);
        if (recebido > 0) await Manual(f).ExecuteAsync(new(f.EmpresaId, f.Pedido.Id, "pix", recebido));
        var pagamentos = f.Pedido.Pagamentos.ToArray();

        item.PrecoUnitario = 100m; item.RecalcularSubtotal(); f.Pedido.RecalcularTotal();
        var resultado = await f.Gerar().ExecuteAsync(new GerarCobrancaPedidoInput(f.EmpresaId, f.Pedido.Id, CobrarSaldo: cobrarSaldo));
        var repetida = await f.Gerar().ExecuteAsync(new GerarCobrancaPedidoInput(f.EmpresaId, f.Pedido.Id, CobrarSaldo: cobrarSaldo));

        resultado.Valor.Should().Be(saldoEsperado);
        resultado.CobrancaId.Should().NotBe(anterior.Id);
        repetida.CobrancaId.Should().Be(resultado.CobrancaId);
        anterior.EstaPendente.Should().BeFalse();
        var atual = f.Cobrancas.Single(c => c.EstaPendente);
        atual.EmpresaId.Should().Be(f.EmpresaId); atual.PedidoId.Should().Be(f.Pedido.Id);
        atual.ConversaId.Should().Be(conversaId); atual.EhOnline.Should().BeFalse(); atual.ExpiraEm.Should().BeNull();
        f.Pedido.TotalPago.Should().Be(recebido);
        f.Pedido.Pagamentos.Should().Equal(pagamentos);
        await f.MpClient.DidNotReceiveWithAnyArgs().CriarPreferenceAsync(default!, default);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReemitirManual_ConsideraCupomAlocadoMesmoSemFlagDeSaldo(bool cobrarSaldo)
    {
        var f = Futuro();
        f.AdicionarNaEntrega();
        f.Creditos.TotaisDoPedidoAsync(f.EmpresaId, f.Pedido.Id, Arg.Any<CancellationToken>()).Returns((0m, 5m));

        var resultado = await f.Gerar().ExecuteAsync(new GerarCobrancaPedidoInput(f.EmpresaId, f.Pedido.Id, CobrarSaldo: cobrarSaldo));

        resultado.Valor.Should().Be(20m);
        f.Pedido.TotalPago.Should().Be(0m);
        await f.MpClient.DidNotReceiveWithAnyArgs().CriarPreferenceAsync(default!, default);
    }

    [Fact]
    public async Task SemMercadoPago_NasceComDividaManualSemPrazoENaoDuplicaAoRepetir()
    {
        var f = Futuro();
        f.MpClient.CriarPreferenceAsync(Arg.Any<CriarPreferenceCommand>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new MercadoPagoNaoConfiguradoException());

        var primeira = await f.Gerar().ExecuteAsync(new GerarCobrancaPedidoInput(f.EmpresaId, f.Pedido.Id));
        var repetida = await f.Gerar().ExecuteAsync(new GerarCobrancaPedidoInput(f.EmpresaId, f.Pedido.Id));

        primeira.Provedor.Should().Be(CobrancaPedido.ProvedorNaEntrega);
        primeira.Valor.Should().Be(25m);
        primeira.LinkPagamento.Should().BeNull();
        primeira.ExpiraEm.Should().BeNull();
        repetida.CobrancaId.Should().Be(primeira.CobrancaId);
        f.Cobrancas.Should().ContainSingle();
        f.Pedido.Status.Should().Be(StatusPedidoMapper.AguardandoPagamento);
    }

    [Fact]
    public async Task ManualExistente_NoDiaDaEntregaPreservaReservaENaoDuplica()
    {
        var f = ReservaNoDiaDaEntrega();
        var manual = f.AdicionarNaEntrega();

        var repetida = await f.Gerar().ExecuteAsync(new GerarCobrancaPedidoInput(f.EmpresaId, f.Pedido.Id));

        repetida.CobrancaId.Should().Be(manual.Id);
        repetida.Reutilizada.Should().BeTrue();
        f.Cobrancas.Should().ContainSingle();
        f.Pedido.Status.Should().Be(StatusPedidoMapper.AguardandoPagamento);
    }

    [Fact]
    public async Task SemMercadoPago_NoDiaDaEntregaPreservaObrigacaoDeSinal()
    {
        var f = ReservaNoDiaDaEntrega();
        f.MpClient.CriarPreferenceAsync(Arg.Any<CriarPreferenceCommand>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new MercadoPagoNaoConfiguradoException());

        var cobranca = await f.Gerar().ExecuteAsync(new GerarCobrancaPedidoInput(f.EmpresaId, f.Pedido.Id));

        cobranca.Provedor.Should().Be(CobrancaPedido.ProvedorNaEntrega);
        cobranca.Valor.Should().Be(25m);
        f.Pedido.Status.Should().Be(StatusPedidoMapper.AguardandoPagamento);
    }

    [Fact]
    public async Task ReservaOnline_NoDiaDaEntregaConservaValorDoSinal()
    {
        var f = ReservaNoDiaDaEntrega();

        var cobranca = await f.Gerar().ExecuteAsync(new GerarCobrancaPedidoInput(f.EmpresaId, f.Pedido.Id));

        cobranca.Valor.Should().Be(12.5m);
        cobranca.Provedor.Should().Be(CobrancaPedido.ProvedorMercadoPago);
    }

    /// <summary>#1717-2: o sinal vira baixa manual só quando o provedor diz que o link não foi pago; em processamento, recusa.</summary>
    [Fact]
    public async Task LinkPendenteEmProcessamento_NoDiaDaEntregaNaoPermiteTrocaParaManual()
    {
        var f = ReservaNoDiaDaEntrega();
        f.AdicionarOnline();
        f.PagamentosNoMercadoPago.Add(new PagamentoMercadoPago("pagamento-sinal", "in_process", null, f.Pedido.Id.ToString(),
            12.5m, CobrancaPedidoFixture.Agora, "pix", "bank_transfer"));

        var trocar = () => f.Trocar().ExecuteAsync(new TrocarFormaPagamentoPedidoInput(f.EmpresaId, f.Pedido.Id, "na_entrega"));

        await trocar.Should().ThrowAsync<UseCaseValidationException>();
        f.Cobrancas.Should().ContainSingle(c => c.EhOnline && c.EstaPendente);
        f.Pedido.Status.Should().Be(StatusPedidoMapper.AguardandoPagamento);
    }

    [Fact]
    public async Task IndisponibilidadeTransitoria_FuturaNaoCriaFallbackManual()
    {
        var f = Futuro();
        f.MpClient.CriarPreferenceAsync(Arg.Any<CriarPreferenceCommand>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("indisponibilidade temporaria"));

        var cobrar = () => f.Gerar().ExecuteAsync(new GerarCobrancaPedidoInput(f.EmpresaId, f.Pedido.Id));

        await cobrar.Should().ThrowAsync<MercadoPagoIndisponivelException>();
        f.Cobrancas.Should().BeEmpty();
    }

    [Fact]
    public async Task NaEntrega_FuturaMantemPedidoAguardandoSinal()
    {
        var f = Futuro();
        var r = await f.Trocar().ExecuteAsync(new TrocarFormaPagamentoPedidoInput(f.EmpresaId, f.Pedido.Id, "na_entrega"));

        r.Cobranca.Valor.Should().Be(25m);
        r.Cobranca.LinkPagamento.Should().BeNull();
        f.Pedido.Status.Should().Be(StatusPedidoMapper.AguardandoPagamento);
    }

    [Theory]
    [InlineData("pix", 25, 12.5)]
    [InlineData("dinheiro", 25, 12.5)]
    [InlineData("credito", 25, 12.5)]
    [InlineData("debito", 25, 12.5)]
    [InlineData("pix", 25.01, 12.51)]
    public async Task BaixaCumulativa_SoLiberaComSinalArredondadoEQuitaNoTotal(string metodo, decimal total, decimal sinal)
    {
        var f = Futuro();
        f.Pedido.Itens.Last().PrecoUnitario = total - 20m;
        f.Pedido.Itens.Last().Subtotal = total - 20m;
        f.Pedido.RecalcularTotal();
        f.PrazoQueries.ObterAsync(f.EmpresaId, f.Pedido.Id, Arg.Any<CancellationToken>())
            .Returns(new PrazoPreparoPedidoLeitura(null, null, [null], TempoPreparoPadraoMinutos: 60, RespiroMinutos: 40));
        f.Cobrancas.Add(CobrancaPedido.CriarNaEntrega(f.EmpresaId, f.Pedido.Id, total, CobrancaPedidoFixture.Agora));
        var uc = Manual(f);

        await uc.ExecuteAsync(new(f.EmpresaId, f.Pedido.Id, metodo, sinal - 0.01m));
        f.Pedido.Status.Should().Be(StatusPedidoMapper.AguardandoPagamento);
        f.Pedido.TotalPago.Should().Be(sinal - 0.01m);
        f.Pedido.InicioPrevistoEm.Should().BeNull();

        await uc.ExecuteAsync(new(f.EmpresaId, f.Pedido.Id, metodo, 0.01m));
        f.Pedido.Status.Should().Be(StatusPedidoMapper.Aguardando);
        f.Pedido.TotalPago.Should().Be(sinal);
        f.Pedido.InicioPrevistoEm.Should().Be(f.Pedido.AgendadoParaEm!.Value.AddMinutes(-100));
        f.Cobrancas.Single().EstaPendente.Should().BeTrue("sinal não é quitação da dívida total");

        await uc.ExecuteAsync(new(f.EmpresaId, f.Pedido.Id, metodo, total - sinal));
        f.Pedido.TotalPago.Should().Be(total);
        f.Cobrancas.Single().ValorPago.Should().Be(total);
        var repetir = () => uc.ExecuteAsync(new(f.EmpresaId, f.Pedido.Id, metodo, total - sinal));
        await repetir.Should().ThrowAsync<UseCaseValidationException>();
        f.Pedido.TotalPago.Should().Be(total);
    }

    [Fact]
    public async Task SinalCompleto_ComExcecaoEsperaAprovacaoEAvisaOperacaoUmaVez()
    {
        var f = Futuro();
        f.Pedido.MarcarRequerAprovacao("fora_de_area");
        f.AdicionarNaEntrega();
        var uc = Manual(f);

        await uc.ExecuteAsync(new(f.EmpresaId, f.Pedido.Id, "pix", 12.49m));
        await f.OperacaoEventos.DidNotReceive().PublicarAsync(EventosOperacao.PedidoMudouStatus,
            Arg.Any<Guid>(), Arg.Any<object>(), Arg.Any<CancellationToken>());
        await uc.ExecuteAsync(new(f.EmpresaId, f.Pedido.Id, "dinheiro", 0.01m));

        f.Pedido.Status.Should().Be(StatusPedidoMapper.AguardandoAprovacaoBaba);
        await f.OperacaoEventos.Received(1).PublicarAsync(EventosOperacao.PedidoMudouStatus, f.EmpresaId,
            Arg.Is<PedidoMudouStatusOperacao>(e => e.PedidoId == f.Pedido.Id
                && e.StatusAntigo == StatusPedidoMapper.AguardandoPagamento
                && e.StatusNovo == StatusPedidoMapper.AguardandoAprovacaoBaba), Arg.Any<CancellationToken>());
        f.Cobrancas.Single().EstaPendente.Should().BeTrue();
    }

    [Fact]
    public async Task EmpresaDivergente_NaoRecebePagamentoNemLiberaFila()
    {
        var f = Futuro();
        f.AdicionarNaEntrega();
        var resposta = await Manual(f).ExecuteAsync(new(Guid.NewGuid(), f.Pedido.Id, "pix", 12.5m));
        resposta.Should().BeNull();
        f.Pedido.TotalPago.Should().Be(0);
        f.Pedido.Status.Should().Be(StatusPedidoMapper.AguardandoPagamento);
    }

    [Fact]
    public async Task Cancelado_NaoRecebeSinalManual()
    {
        var f = Futuro();
        f.AdicionarNaEntrega();
        f.Pedido.Cancelar();
        var receber = () => Manual(f).ExecuteAsync(new(f.EmpresaId, f.Pedido.Id, "pix", 12.5m));
        await receber.Should().ThrowAsync<UseCaseValidationException>();
        f.Pedido.TotalPago.Should().Be(0);
        f.Pedido.Status.Should().Be(StatusPedidoMapper.Cancelado);
    }

    [Fact]
    public async Task LinkOnlinePendente_NaoPermiteBaixaManualMesmoComCobrancaManual()
    {
        var f = Futuro();
        f.AdicionarNaEntrega();
        f.AdicionarOnline();
        var receber = () => Manual(f).ExecuteAsync(new(f.EmpresaId, f.Pedido.Id, "pix", 12.5m));
        await receber.Should().ThrowAsync<UseCaseValidationException>();
        f.Pedido.TotalPago.Should().Be(0);
    }

    private static CobrancaPedidoFixture Futuro()
    {
        var f = new CobrancaPedidoFixture();
        f.Pedido.CriadoEm = CobrancaPedidoFixture.Agora;
        f.Pedido.AgendadoParaEm = f.Pedido.CriadoEm.AddDays(2);
        return f;
    }

    private static CobrancaPedidoFixture ReservaNoDiaDaEntrega()
    {
        var f = new CobrancaPedidoFixture();
        f.Pedido.CriadoEm = CobrancaPedidoFixture.Agora.AddDays(-1);
        f.Pedido.AgendadoParaEm = CobrancaPedidoFixture.Agora;
        return f;
    }

    private static RegistrarPagamentoPedidoUseCase Manual(CobrancaPedidoFixture f, AvisoStatusPedidoCliente? aviso = null) =>
        new(f.PedidoRepo, f.Uow, NullLogger<RegistrarPagamentoPedidoUseCase>.Instance,
            new CalculadoraInicioPrevistoPedido(f.PrazoQueries),
            QuitacaoPedidoTeste.Criar(f.PedidoRepo, f.CobrancaRepo, f.ConversaRepo, f.ImpressaoRepo, f.Publicador, f.OperacaoEventos, f.PrazoQueries),
            avisoCliente: aviso is null ? null : new Lazy<AvisoStatusPedidoCliente>(() => aviso));

    // #2007 (item 6): o sinal registrado à mão avisa o cliente como o pago pelo Mercado Pago, uma vez: só quando
    // o sinal se completa. Parcela abaixo do sinal e a quitação do restante não mandam o aviso do sinal.
    [Fact]
    public async Task SinalManual_AvisaOClienteUmaVezComOSinalEORestante()
    {
        var f = Futuro();
        var total = f.Pedido.Total.Valor;
        var sinal = PlanoPagamentoEncomenda.Sinal(total);
        f.Cobrancas.Add(CobrancaPedido.CriarNaEntrega(f.EmpresaId, f.Pedido.Id, total, CobrancaPedidoFixture.Agora));
        var uc = Manual(f, f.AvisoCliente());

        await uc.ExecuteAsync(new(f.EmpresaId, f.Pedido.Id, "pix", sinal - 0.01m));
        MensagensDoSinal(f).Should().BeEmpty();
        await uc.ExecuteAsync(new(f.EmpresaId, f.Pedido.Id, "pix", 0.01m));
        await uc.ExecuteAsync(new(f.EmpresaId, f.Pedido.Id, "dinheiro", total - sinal));

        MensagensDoSinal(f).Should().ContainSingle().Which.Should().Be(
            ConfirmarPagamentoPedidoUseCase.TextoSinalRecebido(sinal, total - sinal));
    }

    private static List<string?> MensagensDoSinal(CobrancaPedidoFixture f) => f.Notificador.ReceivedCalls()
        .Where(c => c.GetMethodInfo().Name == "EnfileirarEventoAsync"
            && (TipoEventoNotificacao)c.GetArguments()[0]! == TipoEventoNotificacao.ComandaAtualizada)
        .Select(c => JsonSerializer.Deserialize<Dictionary<string, string?>>((string)c.GetArguments()[2]!)!
            .GetValueOrDefault("mensagem"))
        .Where(m => m?.StartsWith("Recebemos o sinal", StringComparison.Ordinal) == true)
        .ToList();
}
