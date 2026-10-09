using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Integration;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Domain.Entities.Financeiro;
using EasyStock.Domain.Enums.Financeiro;
using EasyStock.Application.Services;
using EasyStock.Application.Services.Pedidos;
using EasyStock.Application.UseCases.AtualizarStatusPedido;
using EasyStock.Application.UseCases.Financeiro.ContasReceber;
using EasyStock.Application.UseCases.Financeiro.Integracao;
using EasyStock.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EasyStock.Application.Tests.UseCases;

public class AtualizarStatusPedidoUseCaseTests
{
    private static (AtualizarStatusPedidoUseCase uc, IPedidoRepository repo, IItemEstoqueRepository itemRepo, IMovimentacaoEstoqueRepository movRepo, IUnitOfWork uow, IPublicadorEventoIntegracao publicador) Build(bool permiteNegativo = true, IOperacaoEventPublisher? operacaoEventos = null, IPrazoPreparoPedidoQueries? prazoQueries = null, IContaReceberRepository? crRepo = null, IVagaOcupadaRepository? vagaRepo = null)
    {
        var pedidoRepo = Substitute.For<IPedidoRepository>();
        var itemRepo = Substitute.For<IItemEstoqueRepository>();
        var movRepo = Substitute.For<IMovimentacaoEstoqueRepository>();
        var uow = Substitute.For<IUnitOfWork>();
        var configRepo = Substitute.For<IConfiguracaoLojaRepository>();
        var contaReceberRepo = crRepo ?? Substitute.For<IContaReceberRepository>();
        var categoriaRepo = Substitute.For<ICategoriaFinanceiraRepository>();
        var publicador = Substitute.For<IPublicadorEventoIntegracao>();
        var criarContaReceber = new CriarContaReceberUseCase(contaReceberRepo, categoriaRepo,
            Substitute.For<ICentroCustoRepository>(), uow, NullLogger<CriarContaReceberUseCase>.Instance);
        var gerarCr = new GerarContaReceberDePedidoUseCase(contaReceberRepo, categoriaRepo, configRepo,
            criarContaReceber, NullLogger<GerarContaReceberDePedidoUseCase>.Instance);
        var opts = Options.Create(new PedidoEstoqueOptions { PermiteEstoqueNegativo = permiteNegativo });
        var integ = new PedidoEstoqueIntegrationService(itemRepo, movRepo, Substitute.For<IPublicadorEventoIntegracao>(), opts, NullLogger<PedidoEstoqueIntegrationService>.Instance);
        var uc = new AtualizarStatusPedidoUseCase(pedidoRepo, integ, configRepo, gerarCr, publicador,
            operacaoEventos ?? Substitute.For<IOperacaoEventPublisher>(), uow, NullLogger<AtualizarStatusPedidoUseCase>.Instance,
            new CalculadoraInicioPrevistoPedido(prazoQueries ?? Substitute.For<IPrazoPreparoPedidoQueries>()),
            new EfeitosCancelamentoPedido(contaReceberRepo, vagaRepo ?? Substitute.For<IVagaOcupadaRepository>(),
                NullLogger<EfeitosCancelamentoPedido>.Instance));
        return (uc, pedidoRepo, itemRepo, movRepo, uow, publicador);
    }

    private static Pedido NovoPedido(Guid empresaId, Guid lojaId, Guid produtoId, decimal qty, string status = "aguardando")
    {
        var p = Pedido.Criar(empresaId, null, lojaId, "web");
        p.Status = status;
        p.Itens.Add(new PedidoItem
        {
            Id = Guid.NewGuid(),
            PedidoId = p.Id,
            ProdutoId = produtoId,
            Nome = "Item",
            Quantidade = qty,
            PrecoUnitario = 10m
        });
        return p;
    }

    [Fact]
    public async Task AguardandoPagamentoParaAguardando_GravaInicioPrevisto()
    {
        // #1230: a dona tira o pedido de "aguardando pagamento" pela troca de status (pagou no Pix
        // direto); o pedido entra na fila com início previsto, senão o aviso de atraso nunca sai.
        var prazo = Substitute.For<IPrazoPreparoPedidoQueries>();
        var (uc, repo, _, _, _, _) = Build(prazoQueries: prazo);
        var empresaId = Guid.NewGuid();
        var pedido = NovoPedido(empresaId, Guid.NewGuid(), Guid.NewGuid(), 1, "aguardando_pagamento");
        var entrega = new DateTime(2026, 10, 2, 15, 0, 0, DateTimeKind.Utc);
        pedido.AgendadoParaEm = entrega;
        repo.GetByIdWithDetailsAsync(empresaId, pedido.Id).Returns(pedido);
        prazo.ObterAsync(empresaId, pedido.Id, Arg.Any<CancellationToken>())
            .Returns(new PrazoPreparoPedidoLeitura(null, null, [null], TempoPreparoPadraoMinutos: 60, RespiroMinutos: 40));

        await uc.ExecuteAsync(new AtualizarStatusPedidoCommand(empresaId, pedido.Id, "aguardando"));

        pedido.InicioPrevistoEm.Should().Be(entrega.AddMinutes(-100));
    }

    [Fact]
    public async Task Cancelar_pela_troca_de_status_cancela_conta_receber_e_libera_vaga()
    {
        // #1506: o KDS/PWA cancela por aqui; antes a ContaReceber seguia aberta e a vaga da
        // janela ficava presa ("esgotado" no site). Mesmos efeitos do CancelarPedidoUseCase.
        var crRepo = Substitute.For<IContaReceberRepository>();
        var vagaRepo = Substitute.For<IVagaOcupadaRepository>();
        var (uc, repo, _, _, uow, _) = Build(crRepo: crRepo, vagaRepo: vagaRepo);
        var empresaId = Guid.NewGuid();
        var pedido = NovoPedido(empresaId, Guid.NewGuid(), Guid.NewGuid(), 1, "entregue");
        repo.GetByIdWithDetailsAsync(empresaId, pedido.Id).Returns(pedido);
        var conta = ContaReceber.Criar(empresaId, null, Guid.NewGuid(), "Pedido", DateTime.UtcNow,
            origem: OrigemContaFinanceira.Pedido, origemRefId: pedido.Id);
        crRepo.GetByOrigemAsync(empresaId, OrigemContaFinanceira.Pedido, pedido.Id).Returns(conta);

        await uc.ExecuteAsync(new AtualizarStatusPedidoCommand(empresaId, pedido.Id, "cancelado"));

        conta.Status.Should().Be(StatusContaFinanceira.Cancelada);
        await crRepo.Received(1).UpdateAsync(conta);
        await vagaRepo.Received(1).LiberarPorPedidoAsync(pedido.Id, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task Avancar_status_nao_toca_conta_receber_nem_vaga()
    {
        var crRepo = Substitute.For<IContaReceberRepository>();
        var vagaRepo = Substitute.For<IVagaOcupadaRepository>();
        var (uc, repo, _, _, _, _) = Build(crRepo: crRepo, vagaRepo: vagaRepo);
        var empresaId = Guid.NewGuid();
        var pedido = NovoPedido(empresaId, Guid.NewGuid(), Guid.NewGuid(), 1);
        repo.GetByIdWithDetailsAsync(empresaId, pedido.Id).Returns(pedido);

        await uc.ExecuteAsync(new AtualizarStatusPedidoCommand(empresaId, pedido.Id, "preparando"));

        await vagaRepo.DidNotReceiveWithAnyArgs().LiberarPorPedidoAsync(default, default!, default);
        await crRepo.DidNotReceiveWithAnyArgs().GetByOrigemAsync(default, default, default);
    }

    [Fact]
    public async Task ForaDaFila_NaoConsultaInicioPrevisto()
    {
        // #1230: só a entrada em "aguardando" calcula; avançar o preparo não gasta consulta.
        var prazo = Substitute.For<IPrazoPreparoPedidoQueries>();
        var (uc, repo, _, _, _, _) = Build(prazoQueries: prazo);
        var empresaId = Guid.NewGuid();
        var pedido = NovoPedido(empresaId, Guid.NewGuid(), Guid.NewGuid(), 1);
        repo.GetByIdWithDetailsAsync(empresaId, pedido.Id).Returns(pedido);

        await uc.ExecuteAsync(new AtualizarStatusPedidoCommand(empresaId, pedido.Id, "preparando"));

        await prazo.DidNotReceiveWithAnyArgs().ObterAsync(default, default, default);
    }

    [Fact]
    public async Task Status_aguardando_para_preparando_nao_mexe_estoque()
    {
        var (uc, repo, itemRepo, movRepo, _, _) = Build();
        var empresaId = Guid.NewGuid();
        var lojaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();
        var pedido = NovoPedido(empresaId, lojaId, produtoId, 2);
        repo.GetByIdWithDetailsAsync(empresaId, pedido.Id).Returns(pedido);

        var cmd = new AtualizarStatusPedidoCommand(empresaId, pedido.Id, "preparando");
        await uc.ExecuteAsync(cmd);

        await movRepo.DidNotReceive().InsertAsync(Arg.Any<MovimentacaoEstoque>());
        pedido.Status.Should().Be("preparando");
    }

    [Fact]
    public async Task Status_para_pronto_desconta_estoque_e_atualiza_status()
    {
        var (uc, repo, itemRepo, movRepo, _, _) = Build();
        var empresaId = Guid.NewGuid();
        var lojaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();
        var pedido = NovoPedido(empresaId, lojaId, produtoId, 2, "preparando");
        repo.GetByIdWithDetailsAsync(empresaId, pedido.Id).Returns(pedido);
        itemRepo.GetByProdutoAsync(empresaId, produtoId).Returns(new[]
        {
            new ItemEstoque
            {
                Id = Guid.NewGuid(),
                EmpresaId = empresaId,
                LojaId = lojaId,
                ProdutoId = produtoId,
                QuantidadeAtual = Quantidade.From(10)
            }
        });

        var cmd = new AtualizarStatusPedidoCommand(empresaId, pedido.Id, "pronto");
        await uc.ExecuteAsync(cmd);

        await movRepo.Received(1).InsertAsync(Arg.Is<MovimentacaoEstoque>(
            m => m.Natureza == NaturezaMovimentacaoEstoque.Venda));
        pedido.Status.Should().Be("pronto");
    }

    [Fact]
    public async Task Transicao_invalida_lanca_e_status_nao_muda()
    {
        var (uc, repo, _, _, _, publicador) = Build();
        var empresaId = Guid.NewGuid();
        var pedido = NovoPedido(empresaId, Guid.NewGuid(), Guid.NewGuid(), 1, "entregue");
        repo.GetByIdWithDetailsAsync(empresaId, pedido.Id).Returns(pedido);

        var cmd = new AtualizarStatusPedidoCommand(empresaId, pedido.Id, "preparando");

        await Assert.ThrowsAsync<UseCaseValidationException>(() => uc.ExecuteAsync(cmd));
        pedido.Status.Should().Be("entregue");
        // Transicao invalida NAO publica evento no outbox.
        await publicador.DidNotReceive().PublicarAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid>(),
            Arg.Any<EasyStock.Application.Events.Pedidos.PedidoMudouStatusEvent>(),
            Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Transicao_valida_publica_PedidoMudouStatus_no_outbox()
    {
        var (uc, repo, _, _, _, publicador) = Build();
        var empresaId = Guid.NewGuid();
        var pedido = NovoPedido(empresaId, Guid.NewGuid(), Guid.NewGuid(), 2, "aguardando");
        repo.GetByIdWithDetailsAsync(empresaId, pedido.Id).Returns(pedido);

        var cmd = new AtualizarStatusPedidoCommand(empresaId, pedido.Id, "preparando",
            UsuarioNome: "Operador", Origem: "web");
        await uc.ExecuteAsync(cmd);

        await publicador.Received(1).PublicarAsync(
            empresaId,
            "pedido.mudou_status",
            "pedido",
            pedido.Id,
            Arg.Is<EasyStock.Application.Events.Pedidos.PedidoMudouStatusEvent>(e =>
                e.StatusAntigo == "aguardando" && e.StatusNovo == "preparando" && e.Origem == "web"),
            Arg.Any<int>(),
            pedido.Id.ToString(),
            Arg.Any<Guid?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cancelar_pedido_entregue_devolve_estoque()
    {
        var (uc, repo, itemRepo, movRepo, _, _) = Build();
        var empresaId = Guid.NewGuid();
        var lojaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();
        var pedido = NovoPedido(empresaId, lojaId, produtoId, 2, "entregue");
        repo.GetByIdWithDetailsAsync(empresaId, pedido.Id).Returns(pedido);

        var item = new ItemEstoque
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            LojaId = lojaId,
            ProdutoId = produtoId,
            QuantidadeAtual = Quantidade.From(8)
        };
        itemRepo.GetByProdutoAsync(empresaId, produtoId).Returns(new[] { item });
        // Simula que houve venda anterior pra devolução ser aplicável
        var itemId = pedido.Itens.Single().Id;
        movRepo.ExisteReferenciaAsync(empresaId, produtoId, $"{pedido.Id}:{itemId}",
                NaturezaMovimentacaoEstoque.Venda, Arg.Any<CancellationToken>())
            .Returns(true);

        var cmd = new AtualizarStatusPedidoCommand(empresaId, pedido.Id, "cancelado");
        await uc.ExecuteAsync(cmd);

        item.QuantidadeAtual!.Value.Should().Be(10); // devolvidos 2 unidades
        pedido.Status.Should().Be("cancelado");
    }

    [Fact]
    public async Task PublicaMudouStatusNaOperacaoAposCommit()
    {
        var operacao = Substitute.For<IOperacaoEventPublisher>();
        var (uc, repo, _, _, uow, _) = Build(operacaoEventos: operacao);
        var empresaId = Guid.NewGuid();
        var pedido = NovoPedido(empresaId, Guid.NewGuid(), Guid.NewGuid(), 1);
        repo.GetByIdWithDetailsAsync(empresaId, pedido.Id).Returns(pedido);

        await uc.ExecuteAsync(new AtualizarStatusPedidoCommand(empresaId, pedido.Id, "preparando"));

        Received.InOrder(() =>
        {
            uow.CommitAsync();
            operacao.PublicarAsync(EventosOperacao.PedidoMudouStatus, empresaId,
                Arg.Is<PedidoMudouStatusOperacao>(e =>
                    e.PedidoId == pedido.Id && e.StatusAntigo == "aguardando" && e.StatusNovo == "preparando"),
                Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task CommitFalhaNaoPublicaNaOperacao()
    {
        var operacao = Substitute.For<IOperacaoEventPublisher>();
        var (uc, repo, _, _, uow, _) = Build(operacaoEventos: operacao);
        var empresaId = Guid.NewGuid();
        var pedido = NovoPedido(empresaId, Guid.NewGuid(), Guid.NewGuid(), 1);
        repo.GetByIdWithDetailsAsync(empresaId, pedido.Id).Returns(pedido);
        uow.CommitAsync().Returns<int>(_ => throw new InvalidOperationException("commit falhou"));

        var act = () => uc.ExecuteAsync(new AtualizarStatusPedidoCommand(empresaId, pedido.Id, "preparando"));

        await act.Should().ThrowAsync<InvalidOperationException>();
        operacao.ReceivedCalls().Should().BeEmpty();
    }
}
