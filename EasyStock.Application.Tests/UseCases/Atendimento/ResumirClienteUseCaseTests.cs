using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.UseCases.Atendimento;
using EasyStock.Application.UseCases.Cliente.Dossie;
using EasyStock.Domain.Entities.Atendimento;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Application.Tests.UseCases.Atendimento;

/// <summary>#1839: resumo do cliente no topo do Resumo do Balcão. O LLM é um fake: nenhuma chamada de rede.</summary>
public class ResumirClienteUseCaseTests
{
    private static readonly DateTime Agora = new(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly IAgenteLlmClient _llm = Substitute.For<IAgenteLlmClient>();
    private readonly IConversaRepository _conversas = Substitute.For<IConversaRepository>();
    private readonly IClienteRepository _clientes = Substitute.For<IClienteRepository>();
    private readonly IClienteCrmRepository _crm = Substitute.For<IClienteCrmRepository>();
    private readonly IHistoricoPedidosClienteQueries _pedidos = Substitute.For<IHistoricoPedidosClienteQueries>();
    private readonly Conversa _conversa;

    public ResumirClienteUseCaseTests()
    {
        _conversa = Conversa.Abrir(_empresaId, "5511999998888", Agora, "Maria");
        _conversas.ObterPorIdAsync(_empresaId, _conversa.Id, Arg.Any<CancellationToken>()).Returns(_conversa);
        _conversas.ListarPorClienteAsync(_empresaId, Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([]);
        _llm.Disponivel.Returns(true);
        _llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>())
            .Returns(new RespostaLlm("end_turn", [new BlocoTextoLlm("Cliente frequente — pede lasanha bolonhesa.")], 300, 30));
    }

    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());

    private ResumirClienteUseCase Criar() => new(_llm,
        new ObterDossieClienteUseCase(_clientes, _crm, _pedidos, Substitute.For<IDomicilioQueries>(), _conversas),
        _cache, NullLogger<ResumirClienteUseCase>.Instance);

    private void ClienteComPedidos()
    {
        var cliente = Cliente.Criar(_empresaId, "Maria");
        _clientes.GetByIdWithDetailsAsync(_empresaId, cliente.Id).Returns(cliente);
        _crm.ListarNotasAsync(_empresaId, cliente.Id, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([]);
        _pedidos.ListarAsync(_empresaId, cliente.Id, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([
            new PedidoResumoCliente(Guid.NewGuid(), "entregue", Agora.AddDays(-3), 90m, [new ItemPedidoResumo("Lasanha Bolonhesa", 2)]),
            new PedidoResumoCliente(Guid.NewGuid(), "entregue", Agora.AddDays(-10), 45m, [new ItemPedidoResumo("Lasanha Bolonhesa", 1)]),
            new PedidoResumoCliente(Guid.NewGuid(), "aguardando_pagamento", Agora, 70m, [new ItemPedidoResumo("Nhoque", 1)]),
        ]);
        _conversa.VincularCliente(cliente.Id);
    }

    [Fact]
    public async Task Lead_sem_compra_nem_nota_responde_a_regra_fixa_sem_chamar_a_IA()
    {
        var resultado = await Criar().ExecuteAsync(_empresaId, _conversa.Id);

        resultado.Should().Be(new ResumoClienteResult(ResumirClienteUseCase.SemCompra, false));
        await _llm.DidNotReceiveWithAnyArgs().EnviarAsync(default!, default);
    }

    [Fact]
    public async Task Cliente_com_historico_recebe_resumo_da_IA_sem_travessao_e_com_o_dossie_no_contexto()
    {
        ClienteComPedidos();

        var resultado = await Criar().ExecuteAsync(_empresaId, _conversa.Id);

        resultado.PorIa.Should().BeTrue();
        resultado.Texto.Should().NotContain("—").And.Contain("lasanha bolonhesa");
        await _llm.Received(1).EnviarAsync(Arg.Is<RequisicaoLlm>(r => r.Ferramentas.Count == 0
            && r.Mensagens.Single().Conteudo.OfType<BlocoTextoLlm>().Single().Texto.Contains("Pedidos: 3")), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IA_desligada_ou_em_falha_devolve_texto_nulo_para_a_tela_mostrar_so_os_numeros()
    {
        ClienteComPedidos();
        _llm.Disponivel.Returns(false);
        (await Criar().ExecuteAsync(_empresaId, _conversa.Id)).Should().Be(new ResumoClienteResult(null, false));

        _llm.Disponivel.Returns(true);
        _llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>()).Returns<RespostaLlm>(_ => throw new HttpRequestException("fora"));
        (await Criar().ExecuteAsync(_empresaId, _conversa.Id)).Should().Be(new ResumoClienteResult(null, false));

        // Timeout do HttpClient chega como TaskCanceledException sem cancelamento de quem pediu.
        _llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>()).Returns<RespostaLlm>(_ => throw new TaskCanceledException("timeout"));
        (await Criar().ExecuteAsync(_empresaId, _conversa.Id)).Should().Be(new ResumoClienteResult(null, false));
    }

    [Fact]
    public async Task Abrir_de_novo_o_mesmo_cliente_nao_paga_outra_chamada()
    {
        ClienteComPedidos();
        await Criar().ExecuteAsync(_empresaId, _conversa.Id);
        await Criar().ExecuteAsync(_empresaId, _conversa.Id);
        await _llm.Received(1).EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dossie_soma_o_total_gasto_das_compras_sem_o_pedido_ainda_nao_pago()
    {
        ClienteComPedidos();
        var dossie = await new ObterDossieClienteUseCase(_clientes, _crm, _pedidos, Substitute.For<IDomicilioQueries>(), _conversas)
            .ObterPorConversaAsync(_empresaId, _conversa.Id);
        dossie!.TotalGasto.Should().Be(135m);
    }
}
