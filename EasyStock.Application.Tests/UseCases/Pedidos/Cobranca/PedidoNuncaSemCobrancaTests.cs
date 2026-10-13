using EasyStock.Application.Ports.Output.Integration;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Services;
using EasyStock.Application.Services.Pedidos;
using EasyStock.Application.UseCases.AtualizarStatusPedido;
using EasyStock.Application.UseCases.CancelarPedido;
using EasyStock.Application.UseCases.Financeiro.ContasReceber;
using EasyStock.Application.UseCases.Financeiro.Integracao;
using EasyStock.Application.UseCases.Pedidos.Cobranca;
using EasyStock.Domain.Enums.Pagamentos;
using EasyStock.Domain.Exceptions.Storefront;
using EasyStock.Domain.Sales;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EasyStock.Application.Tests.UseCases.Pedidos.Cobranca;

/// <summary>
/// #1726 (decisão do Felipe, opção A): o pedido confirmado pela atendente nunca nasce sem cobrança, e liberar
/// sem pagamento um pedido com link pendente cancela o link, conserva a dívida e registra quem liberou.
/// </summary>
public class PedidoNuncaSemCobrancaTests
{
    [Fact]
    public async Task Provedor_fora_na_confirmacao_pelo_atendimento_deixa_a_divida_na_entrega()
    {
        var f = new CobrancaPedidoFixture();
        var conversa = Guid.NewGuid();
        f.MpClient.CriarPreferenceAsync(Arg.Any<CriarPreferenceCommand>(), Arg.Any<CancellationToken>())
            .Returns<PreferenceCriadaResult>(_ => throw new HttpRequestException("provedor fora"));

        var r = await f.Gerar().ExecuteAsync(new GerarCobrancaPedidoInput(f.EmpresaId, f.Pedido.Id, conversa,
            CobrarSaldo: true, NaEntregaSemProvedor: true));

        var cobranca = f.Cobrancas.Should().ContainSingle().Subject;
        cobranca.EhOnline.Should().BeFalse();
        cobranca.EstaPendente.Should().BeTrue();
        cobranca.Valor.Should().Be(25m);
        cobranca.ConversaId.Should().Be(conversa);
        r.LinkPagamento.Should().BeNull();
    }

    [Fact]
    public async Task Provedor_fora_sem_a_confirmacao_assistida_continua_recusando()
    {
        var f = new CobrancaPedidoFixture();
        f.MpClient.CriarPreferenceAsync(Arg.Any<CriarPreferenceCommand>(), Arg.Any<CancellationToken>())
            .Returns<PreferenceCriadaResult>(_ => throw new HttpRequestException("provedor fora"));

        await f.Gerar().Invoking(g => g.ExecuteAsync(new GerarCobrancaPedidoInput(f.EmpresaId, f.Pedido.Id, CobrarSaldo: true)))
            .Should().ThrowAsync<MercadoPagoIndisponivelException>();
        f.Cobrancas.Should().BeEmpty();
    }

    [Fact]
    public async Task Liberar_sem_pagamento_cancela_o_link_conserva_a_divida_e_registra_quem_liberou()
    {
        var f = new CobrancaPedidoFixture();
        var online = f.AdicionarOnline(conversaId: Guid.NewGuid());
        var quem = Guid.NewGuid();

        await AtualizarStatus(f).ExecuteAsync(new AtualizarStatusPedidoCommand(f.EmpresaId, f.Pedido.Id, "aguardando", quem, "Bia"));

        online.Status.Should().Be(StatusCobrancaPedido.Cancelada);
        f.Cobrancas.Should().ContainSingle(c => c.EstaPendente).Which.EhOnline.Should().BeFalse();
        f.Pedido.Status.Should().Be(StatusPedidoMapper.Aguardando);
        f.Eventos.Should().ContainSingle(e => e.Tipo == "forma_pagamento_trocada").Which.UsuarioNome.Should().Be("Bia");
        await f.MpClient.Received(1).ExpirarPreferenciaAsync("pref-antiga", Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Liberar_pedido_de_excecao_ainda_espera_a_dona()
    {
        var f = new CobrancaPedidoFixture();
        f.Pedido.MarcarRequerAprovacao("fora da área de entrega");
        f.AdicionarOnline();

        await AtualizarStatus(f).ExecuteAsync(new AtualizarStatusPedidoCommand(f.EmpresaId, f.Pedido.Id, "aguardando", Guid.NewGuid(), "Bia"));

        f.Pedido.Status.Should().Be(StatusPedidoMapper.AguardandoAprovacaoBaba);
    }

    [Fact]
    public async Task Sem_link_pendente_a_troca_de_status_segue_como_antes()
    {
        var f = new CobrancaPedidoFixture();

        await AtualizarStatus(f).ExecuteAsync(new AtualizarStatusPedidoCommand(f.EmpresaId, f.Pedido.Id, "aguardando", Guid.NewGuid(), "Bia"));

        f.Pedido.Status.Should().Be(StatusPedidoMapper.Aguardando);
        f.Cobrancas.Should().BeEmpty();
        f.Eventos.Should().ContainSingle(e => e.Tipo == "status_changed");
    }

    private static AtualizarStatusPedidoUseCase AtualizarStatus(CobrancaPedidoFixture f)
    {
        var contas = Substitute.For<IContaReceberRepository>();
        var categorias = Substitute.For<ICategoriaFinanceiraRepository>();
        var config = Substitute.For<IConfiguracaoLojaRepository>();
        var estoque = new PedidoEstoqueIntegrationService(Substitute.For<IItemEstoqueRepository>(),
            Substitute.For<IMovimentacaoEstoqueRepository>(), Substitute.For<IPublicadorEventoIntegracao>(),
            Options.Create(new PedidoEstoqueOptions()), NullLogger<PedidoEstoqueIntegrationService>.Instance);
        var gerarConta = new GerarContaReceberDePedidoUseCase(contas, categorias, config,
            new CriarContaReceberUseCase(contas, categorias, Substitute.For<ICentroCustoRepository>(), f.Uow,
                NullLogger<CriarContaReceberUseCase>.Instance), NullLogger<GerarContaReceberDePedidoUseCase>.Instance);
        var cancelar = new CancelarPedidoUseCase(f.PedidoRepo, estoque,
            new EfeitosCancelamentoPedido(contas, f.VagaRepo, NullLogger<EfeitosCancelamentoPedido>.Instance), f.Uow,
            NullLogger<CancelarPedidoUseCase>.Instance, f.CobrancaRepo, f.Publicador, f.OperacaoEventos);
        return new AtualizarStatusPedidoUseCase(f.PedidoRepo, estoque, config, gerarConta, f.Publicador, f.OperacaoEventos, f.Uow,
            NullLogger<AtualizarStatusPedidoUseCase>.Instance, new CalculadoraInicioPrevistoPedido(f.PrazoQueries), cancelar,
            f.CobrancaRepo, f.Trocar());
    }
}
