using System.Text.Json;
using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento.Ferramentas;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Tests.Services.Storefront;
using EasyStock.Application.UseCases.Cliente.Dossie;
using EasyStock.Domain.Entities.Atendimento;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Application.Tests.Services.Atendimento.Ferramentas;

/// <summary>
/// <c>criar_pedido</c> (S06 ligada na S11): núcleo do checkout (S10). Desde #2544 (S73) cria o pedido sem link e
/// devolve o resumo; a cobrança sai por <c>enviar_link_pagamento</c>.
/// </summary>
public class CriarPedidoFerramentaTests
{
    private static readonly DateTime Agora = new(2026, 6, 1, 15, 0, 0, DateTimeKind.Utc);

    private sealed class Cenario
    {
        public CheckoutCoreServiceTests.Cenario Checkout { get; } = new();
        public Cliente Cliente { get; }
        public Conversa Conversa { get; }
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

            var uow = Uow;
            Ferramenta = new CriarPedidoFerramenta(
                Checkout.CriarPedidoAtendimento(conversaRepo, clienteRepo, uow),
                clienteRepo, Checkout.StorefrontRepo, Checkout.JanelaRepo);
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
        await cloud.DidNotReceiveWithAnyArgs().EnviarTextoAsync(default!, default!, default, default);
    }

    [Fact]
    public async Task CriaPedidoSemLink_EOrientaOResumoAntesDoPagamento()
    {
        // #2544 (S73): o link só sai depois do ok ao resumo, por enviar_link_pagamento.
        var c = new Cenario();

        var resultado = await c.ExecutarAsync(new
        {
            itens = new[] { new { cardapio_item_id = c.Checkout.CardapioItemId, quantidade = 2, observacao = "sem granulado" } },
            data_entrega = c.Checkout.DataEntrega.ToString("yyyy-MM-dd"),
            janela_id = c.Checkout.JanelaId,
        });

        using var json = JsonDocument.Parse(resultado);
        json.RootElement.TryGetProperty("erro", out _).Should().BeFalse(resultado);
        json.RootElement.TryGetProperty("linkPagamento", out _).Should().BeFalse(resultado);
        json.RootElement.GetProperty("pedidoId").GetString().Should().NotBeNullOrWhiteSpace();
        json.RootElement.GetProperty("total").GetString().Should().NotBeNullOrWhiteSpace();
        json.RootElement.GetProperty("itens")[0].GetProperty("observacao").GetString().Should().Be("sem granulado");
        json.RootElement.GetProperty("orientacao").GetString().Should().Contain("enviar_resumo_pedido");
        c.Conversa.PedidoEmAndamentoId.Should().NotBeNull();
        c.Checkout.PedidosAdicionados.Should().ContainSingle();
        c.Contexto!.RespostaVerificada.Should().BeNull("sem link não há resposta pronta para substituir a do agente");
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
    }
}
