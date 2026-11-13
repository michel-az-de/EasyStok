using System.Text.Json;
using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Pagamentos;
using EasyStock.Application.Services.Atendimento.Ferramentas;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Services.Storefront;
using EasyStock.Application.Tests.Services.Storefront;
using EasyStock.Application.UseCases.Cliente.Dossie;
using EasyStock.Application.UseCases.Pedidos.Cobranca;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Entities.Pagamentos;
using EasyStock.Domain.Sales;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute.ExceptionExtensions;

namespace EasyStock.Application.Tests.Services.Atendimento.Ferramentas;

/// <summary>
/// <c>criar_pedido</c> (S06 ligada na S11): núcleo do checkout (S10) + cobrança (S11) e devolve resumo,
/// total e link para o agente responder.
/// </summary>
public class CriarPedidoFerramentaTests
{
    private static readonly DateTime Agora = new(2026, 6, 1, 15, 0, 0, DateTimeKind.Utc);

    private sealed class Cenario
    {
        public CheckoutCoreServiceTests.Cenario Checkout { get; } = new();
        public Cliente Cliente { get; }
        public Conversa Conversa { get; }
        public List<CobrancaPedido> Cobrancas { get; } = new();
        public IMercadoPagoClient Mp { get; } = Substitute.For<IMercadoPagoClient>();
        public IUnitOfWork Uow { get; } = Substitute.For<IUnitOfWork>();
        public IConversaRepository Conversas { get; } = Substitute.For<IConversaRepository>();
        public IClienteRepository Clientes { get; } = Substitute.For<IClienteRepository>();
        public CriarPedidoFerramenta Ferramenta { get; }

        public Cenario()
        {
            var empresaId = Checkout.Storefront.EmpresaId;
            Cliente = new Cliente { Id = Guid.NewGuid(), EmpresaId = empresaId, Nome = "Maria" };
            Cliente.Enderecos.Add(new ClienteEndereco { Id = Guid.NewGuid(), ClienteId = Cliente.Id, Cep = "01310-100", Padrao = true });
            var clienteRepo = Clientes;
            clienteRepo.GetByIdWithDetailsAsync(empresaId, Cliente.Id).Returns(Cliente);

            Conversa = Conversa.Abrir(empresaId, "5511999998888", Agora, "Maria", Cliente.Id);
            var conversaRepo = Conversas;
            conversaRepo.ObterPorIdAsync(empresaId, Conversa.Id, Arg.Any<CancellationToken>()).Returns(Conversa);
            conversaRepo.ObterPorIdComLockAsync(empresaId, Conversa.Id, Arg.Any<CancellationToken>()).Returns(Conversa);

            var cobrancaRepo = Substitute.For<ICobrancaPedidoRepository>();
            cobrancaRepo.ListarDoPedidoAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(_ => Cobrancas.ToList());
            cobrancaRepo.When(r => r.AddAsync(Arg.Any<CobrancaPedido>(), Arg.Any<CancellationToken>()))
                .Do(ci => Cobrancas.Add(ci.Arg<CobrancaPedido>()));

            Mp.CriarPreferenceAsync(Arg.Any<CriarPreferenceCommand>(), Arg.Any<CancellationToken>())
                .Returns(new PreferenceCriadaResult("pref-1", "https://mp.test/pref-1"));

            var uow = Uow;
            var gerar = new GerarCobrancaPedidoUseCase(Substitute.For<IPedidoRepository>(), Checkout.StorefrontRepo,
                cobrancaRepo, Mp, Checkout.Servico(), uow, TimeProvider.System, NullLogger<GerarCobrancaPedidoUseCase>.Instance);
            Ferramenta = new CriarPedidoFerramenta(
                Checkout.CriarPedidoAtendimento(conversaRepo, clienteRepo, uow),
                gerar, clienteRepo, Checkout.StorefrontRepo, Checkout.JanelaRepo);
            Uow.ExecuteInTransactionSemRetryAsync(Arg.Any<Func<CancellationToken, Task<string>>>(), Arg.Any<CancellationToken>())
                .Returns(ci => ci.Arg<Func<CancellationToken, Task<string>>>()(default));
            Uow.ExecuteInTransactionSemRetryAsync(Arg.Any<Func<CancellationToken, Task<ResultadoTurnoAgente>>>(), Arg.Any<CancellationToken>())
                .Returns(ci => ci.Arg<Func<CancellationToken, Task<ResultadoTurnoAgente>>>()(default));
        }

        public AgenteAtendimentoService Agente(IAgenteLlmClient llm, IWhatsAppCloudClient cloud)
        {
            var empresa = Checkout.Storefront.EmpresaId;
            var config = ConfiguracaoAtendimento.CriarPadrao(empresa);
            config.Ativo = true;
            Checkout.ConfiguracaoAtendimentoRepo.GetByEmpresaIdAsync(empresa).Returns(config);
            Conversa.RegistrarEntrada(Agora);
            Conversas.ObterComMensagensAsync(empresa, Conversa.Id, Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(new ConversaComMensagens(Conversa, [Mensagem.Entrada(empresa, Conversa.Id, Agora,
                    EasyStock.Domain.Enums.Atendimento.TipoConteudoMensagem.Texto, "Pode fechar", "wamid.pedido")]));
            Conversas.ObterSituacaoAsync(empresa, Conversa.Id, Arg.Any<CancellationToken>()).Returns(_ => Conversa.Situacao);
            var dossie = new ObterDossieClienteUseCase(Clientes, Substitute.For<IClienteCrmRepository>(),
                Substitute.For<IHistoricoPedidosClienteQueries>(), Substitute.For<IDomicilioQueries>(), Conversas);
            return new AgenteAtendimentoService(llm, Conversas, Checkout.ConfiguracaoAtendimentoRepo, Clientes,
                [Ferramenta], Substitute.For<IEscaladorConversa>(), cloud, Substitute.For<IUsoIaRepository>(), Uow,
                dossie, Substitute.For<ICadernoRepository>(), NullLogger<AgenteAtendimentoService>.Instance);
        }

        public ContextoTurnoAgente? Contexto { get; private set; }
        public Task<string> ExecutarAsync(object entrada)
        {
            Contexto = new ContextoTurnoAgente(Checkout.Storefront.EmpresaId, Conversa, Agora);
            return Ferramenta.ExecutarAsync(Contexto, JsonSerializer.SerializeToElement(entrada));
        }
    }

    [Fact]
    public async Task Agente_grava_pedido_antes_do_provedor_e_falha_posterior_nao_desfaz_reserva_duravel()
    {
        var c = new Cenario();
        var empresa = c.Checkout.Storefront.EmpresaId;
        var transacao = false;
        var pedidosDuraveis = 0;
        var provedorConfirmou = false;
        var falhouDepoisDoProvedor = false;
        async Task<T> Transacao<T>(Func<CancellationToken, Task<T>> executar)
        {
            if (transacao) return await executar(default); // mesma semântica de reuso do UoW real
            transacao = true;
            try
            {
                var resultado = await executar(default);
                pedidosDuraveis = c.Checkout.PedidosAdicionados.Count;
                return resultado;
            }
            finally { transacao = false; }
        }
        c.Uow.ExecuteInTransactionSemRetryAsync(Arg.Any<Func<CancellationToken, Task<PedidoReservado>>>(), Arg.Any<CancellationToken>())
            .Returns(ci => Transacao(ci.Arg<Func<CancellationToken, Task<PedidoReservado>>>()));
        c.Uow.ExecuteInTransactionSemRetryAsync(Arg.Any<Func<CancellationToken, Task<string>>>(), Arg.Any<CancellationToken>())
            .Returns(ci => Transacao(ci.Arg<Func<CancellationToken, Task<string>>>()));
        c.Uow.ExecuteInTransactionSemRetryAsync(Arg.Any<Func<CancellationToken, Task<ResultadoTurnoAgente>>>(), Arg.Any<CancellationToken>())
            .Returns(ci => Transacao(ci.Arg<Func<CancellationToken, Task<ResultadoTurnoAgente>>>()));
        c.Uow.CommitAsync().Returns(_ =>
        {
            if (provedorConfirmou && !falhouDepoisDoProvedor)
            {
                falhouDepoisDoProvedor = true;
                throw new InvalidOperationException("falha controlada após preferência aceita");
            }
            return 1;
        });
        c.Mp.CriarPreferenceAsync(Arg.Any<CriarPreferenceCommand>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            transacao.Should().BeFalse("a transação do pedido deve fechar antes da rede de pagamento");
            pedidosDuraveis.Should().Be(1);
            provedorConfirmou = true;
            return new PreferenceCriadaResult("pref-controlada", "https://mp.test/controlada");
        });
        var entrada = JsonSerializer.SerializeToElement(new
        {
            itens = new[] { new { cardapio_item_id = c.Checkout.CardapioItemId, quantidade = 1 } },
            data_entrega = c.Checkout.DataEntrega.ToString("yyyy-MM-dd"), janela_id = c.Checkout.JanelaId,
        });
        var llm = Substitute.For<IAgenteLlmClient>();
        llm.Disponivel.Returns(true);
        llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>())
            .Returns(new RespostaLlm("tool_use", [new BlocoUsoFerramentaLlm("tool-1", "criar_pedido", entrada)], 5, 5));
        var cloud = Substitute.For<IWhatsAppCloudClient>();
        var agente = c.Agente(llm, cloud);

        var resultado = await agente.ProcessarTurnoAsync(empresa, c.Conversa.Id, Agora);

        provedorConfirmou.Should().BeTrue();
        falhouDepoisDoProvedor.Should().BeTrue();
        pedidosDuraveis.Should().Be(1);
        c.Checkout.PedidosAdicionados.Should().ContainSingle().Which.Status.Should().Be(StatusPedidoMapper.AguardandoPagamento);
        c.Conversa.PedidoEmAndamentoId.Should().NotBeNull();
        resultado.Respondeu.Should().BeFalse();
        c.Uow.Received(1).DescartarAlteracoesPendentes();
        await cloud.DidNotReceiveWithAnyArgs().EnviarTextoAsync(default!, default!, default, default);
    }

    [Fact]
    public async Task Identidade_muda_entre_guard_e_reserva_interrompe_sem_segunda_llm_ou_efeito()
    {
        var c = new Cenario();
        var empresa = c.Checkout.Storefront.EmpresaId;
        c.Conversas.TravarParaPedidoAsync(empresa, c.Conversa.Id, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            c.Conversa.CorrigirVinculoCliente(null, c.Conversa.VinculoRevisao);
            c.Conversa.CorrigirVinculoCliente(c.Cliente.Id, c.Conversa.VinculoRevisao);
            return (Guid?)null;
        });
        var entrada = JsonSerializer.SerializeToElement(new
        {
            itens = new[] { new { cardapio_item_id = c.Checkout.CardapioItemId, quantidade = 1 } },
            data_entrega = c.Checkout.DataEntrega.ToString("yyyy-MM-dd"), janela_id = c.Checkout.JanelaId,
        });
        var llm = Substitute.For<IAgenteLlmClient>();
        llm.Disponivel.Returns(true);
        llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>())
            .Returns(new RespostaLlm("tool_use", [new BlocoUsoFerramentaLlm("tool-1", "criar_pedido", entrada)], 5, 5));
        var cloud = Substitute.For<IWhatsAppCloudClient>();

        var resultado = await c.Agente(llm, cloud).ProcessarTurnoAsync(empresa, c.Conversa.Id, Agora);

        resultado.Respondeu.Should().BeFalse();
        c.Checkout.PedidosAdicionados.Should().BeEmpty();
        await llm.Received(1).EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>());
        await c.Mp.DidNotReceiveWithAnyArgs().CriarPreferenceAsync(default!, default);
        await cloud.DidNotReceiveWithAnyArgs().EnviarTextoAsync(default!, default!, default, default);
    }

    [Fact]
    public async Task CriaPedidoEDevolveResumoTotalELink()
    {
        var c = new Cenario();

        var resultado = await c.ExecutarAsync(new
        {
            itens = new[] { new { cardapio_item_id = c.Checkout.CardapioItemId, quantidade = 2, observacao = "sem granulado" } },
            data_entrega = c.Checkout.DataEntrega.ToString("yyyy-MM-dd"),
            janela_id = c.Checkout.JanelaId,
        });

        using var json = JsonDocument.Parse(resultado);
        json.RootElement.TryGetProperty("erro", out _).Should().BeFalse(resultado);
        json.RootElement.GetProperty("linkPagamento").GetString().Should().Be("https://mp.test/pref-1");
        json.RootElement.GetProperty("total").GetString().Should().NotBeNullOrWhiteSpace();
        json.RootElement.GetProperty("itens")[0].GetProperty("observacao").GetString().Should().Be("sem granulado");
        c.Conversa.PedidoEmAndamentoId.Should().NotBeNull();
        c.Cobrancas.Should().ContainSingle().Which.ConversaId.Should().Be(c.Conversa.Id);
        c.Contexto!.RespostaVerificada.Should().Contain("Pedido registrado").And.Contain("Aguardando pagamento")
            .And.Contain("https://mp.test/pref-1").And.NotContain("sem granulado");
    }

    [Fact]
    public async Task SemClienteIdentificado_DevolveErro()
    {
        var c = new Cenario();
        var anonima = Conversa.Abrir(c.Checkout.Storefront.EmpresaId, "5511911112222", Agora);

        var resultado = await c.Ferramenta.ExecutarAsync(
            new ContextoTurnoAgente(c.Checkout.Storefront.EmpresaId, anonima, Agora),
            JsonSerializer.SerializeToElement(new { itens = Array.Empty<object>(), data_entrega = "2026-06-02" }));

        resultado.Should().Contain("cliente_nao_identificado");
    }

    [Fact]
    public async Task Snapshot_do_turno_recusa_pedido_apos_retirar_e_restaurar_a_mesma_pessoa()
    {
        var c = new Cenario();
        var contexto = new ContextoTurnoAgente(c.Checkout.Storefront.EmpresaId, c.Conversa, Agora);
        c.Conversa.CorrigirVinculoCliente(null, c.Conversa.VinculoRevisao);
        c.Conversa.CorrigirVinculoCliente(c.Cliente.Id, c.Conversa.VinculoRevisao);
        var resultado = await c.Ferramenta.ExecutarAsync(contexto, JsonSerializer.SerializeToElement(new
        {
            itens = new[] { new { cardapio_item_id = c.Checkout.CardapioItemId, quantidade = 1 } },
            data_entrega = c.Checkout.DataEntrega.ToString("yyyy-MM-dd"), janela_id = c.Checkout.JanelaId,
        }));

        resultado.Should().Contain("contexto_alterado");
        contexto.Interrompido.Should().BeTrue();
        c.Checkout.PedidosAdicionados.Should().BeEmpty();
        await c.Mp.DidNotReceiveWithAnyArgs().CriarPreferenceAsync(default!, default);
    }

    [Fact]
    public async Task ClienteBloqueado_RecusaComCodigoSemCriarPedido()
    {
        var c = new Cenario();
        c.Cliente.Bloquear("golpe", Agora);

        var resultado = await c.ExecutarAsync(new
        {
            itens = new[] { new { cardapio_item_id = c.Checkout.CardapioItemId, quantidade = 1 } },
            data_entrega = c.Checkout.DataEntrega.ToString("yyyy-MM-dd"),
            janela_id = c.Checkout.JanelaId,
        });

        using var json = JsonDocument.Parse(resultado);
        json.RootElement.GetProperty("erro").GetString().Should().Be("cliente_bloqueado");
        resultado.Should().NotContain("golpe", "o motivo do bloqueio é interno");
        c.Conversa.PedidoEmAndamentoId.Should().BeNull();
        c.Cobrancas.Should().BeEmpty();
    }

    [Fact]
    public async Task MercadoPagoFora_DesfazPedidoEVagaEOrientaTentarDeNovo()
    {
        // #1301: o link não saía, o pedido ficava em AguardandoPagamento sem cobrança e a vaga presa.
        var c = new Cenario();
        c.Mp.CriarPreferenceAsync(Arg.Any<CriarPreferenceCommand>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("500"));

        var resultado = await c.ExecutarAsync(new
        {
            itens = new[] { new { cardapio_item_id = c.Checkout.CardapioItemId, quantidade = 1 } },
            data_entrega = c.Checkout.DataEntrega.ToString("yyyy-MM-dd"),
            janela_id = c.Checkout.JanelaId,
        });

        using var json = JsonDocument.Parse(resultado);
        json.RootElement.GetProperty("erro").GetString().Should().Be("pagamento_indisponivel");
        json.RootElement.GetProperty("orientacao").GetString().Should().Contain("criar_pedido");
        var pedido = c.Checkout.PedidosAdicionados.Should().ContainSingle().Subject;
        pedido.Status.Should().Be(StatusPedidoMapper.Cancelado);
        c.Contexto!.RespostaVerificada.Should().BeNull("a falha do provedor desfez o pedido");
        await c.Checkout.VagaRepo.Received(1).LiberarPorPedidoAsync(
            pedido.Id, Arg.Is<string>(m => m.Contains("mercado_pago_indisponivel")), Arg.Any<CancellationToken>());
    }
}
