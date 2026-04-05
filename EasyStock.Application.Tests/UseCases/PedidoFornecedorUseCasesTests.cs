using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.PedidoFornecedor;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Exceptions;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace EasyStock.Application.Tests.UseCases;

public class PedidoFornecedorUseCasesTests
{
    // ──────────────────────────────────────────────
    // Criar
    // ──────────────────────────────────────────────

    [Fact]
    public async Task Deve_criar_pedido_com_itens()
    {
        var fornecedorRepo = Substitute.For<IFornecedorRepository>();
        var pedidoRepo = Substitute.For<IPedidoFornecedorRepository>();
        var itemRepo = Substitute.For<IItemPedidoFornecedorRepository>();
        var uow = Substitute.For<IUnitOfWork>();
        var logger = Substitute.For<ILogger<CriarPedidoFornecedorUseCase>>();
        var useCase = new CriarPedidoFornecedorUseCase(fornecedorRepo, pedidoRepo, itemRepo, uow, logger);

        var empresaId = Guid.NewGuid();
        var fornecedorId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();

        fornecedorRepo.GetByIdAsync(empresaId, fornecedorId).Returns(new Fornecedor
        {
            Id = fornecedorId,
            EmpresaId = empresaId,
            Nome = "Fornecedor Teste",
            Ativo = true
        });

        var command = new CriarPedidoFornecedorCommand(
            empresaId,
            fornecedorId,
            DateTime.UtcNow.AddDays(10),
            500m,
            "Email",
            "BR123",
            null,
            new[]
            {
                new ItemPedidoFornecedorInput(produtoId, null, "Produto A", 10, 50m),
                new ItemPedidoFornecedorInput(null, null, "Item sem produto", 5, null)
            });

        var result = await useCase.ExecuteAsync(command);

        result.Should().NotBeNull();
        result.FornecedorId.Should().Be(fornecedorId);
        result.Status.Should().Be(StatusPedidoFornecedor.Aberto);
        result.Itens.Should().HaveCount(2);
        await pedidoRepo.Received(1).AddAsync(Arg.Any<PedidoFornecedor>());
        await itemRepo.Received(1).AddRangeAsync(Arg.Is<IEnumerable<ItemPedidoFornecedor>>(itens => itens.Count() == 2));
        await uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task Deve_rejeitar_criar_pedido_para_fornecedor_inativo()
    {
        var fornecedorRepo = Substitute.For<IFornecedorRepository>();
        var pedidoRepo = Substitute.For<IPedidoFornecedorRepository>();
        var itemRepo = Substitute.For<IItemPedidoFornecedorRepository>();
        var uow = Substitute.For<IUnitOfWork>();
        var logger = Substitute.For<ILogger<CriarPedidoFornecedorUseCase>>();
        var useCase = new CriarPedidoFornecedorUseCase(fornecedorRepo, pedidoRepo, itemRepo, uow, logger);

        var empresaId = Guid.NewGuid();
        var fornecedorId = Guid.NewGuid();

        fornecedorRepo.GetByIdAsync(empresaId, fornecedorId).Returns(new Fornecedor
        {
            Id = fornecedorId,
            EmpresaId = empresaId,
            Nome = "Fornecedor Inativo",
            Ativo = false
        });

        var command = new CriarPedidoFornecedorCommand(
            empresaId, fornecedorId, null, null, null, null, null,
            new[] { new ItemPedidoFornecedorInput(null, null, "Item", 1, null) });

        await Assert.ThrowsAsync<UseCaseValidationException>(() => useCase.ExecuteAsync(command));
        await uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task Deve_rejeitar_criar_pedido_sem_itens()
    {
        var fornecedorRepo = Substitute.For<IFornecedorRepository>();
        var pedidoRepo = Substitute.For<IPedidoFornecedorRepository>();
        var itemRepo = Substitute.For<IItemPedidoFornecedorRepository>();
        var uow = Substitute.For<IUnitOfWork>();
        var logger = Substitute.For<ILogger<CriarPedidoFornecedorUseCase>>();
        var useCase = new CriarPedidoFornecedorUseCase(fornecedorRepo, pedidoRepo, itemRepo, uow, logger);

        var command = new CriarPedidoFornecedorCommand(
            Guid.NewGuid(), Guid.NewGuid(), null, null, null, null, null, Array.Empty<ItemPedidoFornecedorInput>());

        var act = () => useCase.ExecuteAsync(command);

        await act.Should().ThrowAsync<UseCaseValidationException>()
            .WithMessage("*ao menos um item*");
    }

    // ──────────────────────────────────────────────
    // Transição de status inválida (domínio)
    // ──────────────────────────────────────────────

    [Theory]
    [InlineData(StatusPedidoFornecedor.Recebido, StatusPedidoFornecedor.EmTransito)]
    [InlineData(StatusPedidoFornecedor.Recebido, StatusPedidoFornecedor.Cancelado)]
    [InlineData(StatusPedidoFornecedor.Cancelado, StatusPedidoFornecedor.EmTransito)]
    [InlineData(StatusPedidoFornecedor.Cancelado, StatusPedidoFornecedor.Recebido)]
    [InlineData(StatusPedidoFornecedor.Aberto, StatusPedidoFornecedor.Recebido)]
    public void PodeTransicionarPara_deve_rejeitar_transicao_invalida(
        StatusPedidoFornecedor statusAtual,
        StatusPedidoFornecedor novoStatus)
    {
        var pedido = new PedidoFornecedor { Status = statusAtual };
        pedido.PodeTransicionarPara(novoStatus).Should().BeFalse();
    }

    [Theory]
    [InlineData(StatusPedidoFornecedor.Aberto, StatusPedidoFornecedor.EmTransito)]
    [InlineData(StatusPedidoFornecedor.Aberto, StatusPedidoFornecedor.Cancelado)]
    [InlineData(StatusPedidoFornecedor.EmTransito, StatusPedidoFornecedor.Recebido)]
    [InlineData(StatusPedidoFornecedor.EmTransito, StatusPedidoFornecedor.Cancelado)]
    public void PodeTransicionarPara_deve_permitir_transicao_valida(
        StatusPedidoFornecedor statusAtual,
        StatusPedidoFornecedor novoStatus)
    {
        var pedido = new PedidoFornecedor { Status = statusAtual };
        pedido.PodeTransicionarPara(novoStatus).Should().BeTrue();
    }

    [Fact]
    public void Cancelar_deve_lancar_excecao_se_status_invalido()
    {
        var pedido = new PedidoFornecedor { Status = StatusPedidoFornecedor.Recebido };
        var act = () => pedido.Cancelar();
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void IniciarTransito_deve_lancar_excecao_se_status_invalido()
    {
        var pedido = new PedidoFornecedor { Status = StatusPedidoFornecedor.Cancelado };
        var act = () => pedido.IniciarTransito();
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void Receber_deve_lancar_excecao_se_status_invalido()
    {
        var pedido = new PedidoFornecedor { Status = StatusPedidoFornecedor.Aberto };
        var act = () => pedido.Receber();
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    // ──────────────────────────────────────────────
    // Transição de status – use case
    // ──────────────────────────────────────────────

    [Fact]
    public async Task Deve_transicionar_para_EmTransito_com_sucesso()
    {
        var pedidoRepo = Substitute.For<IPedidoFornecedorRepository>();
        var uow = Substitute.For<IUnitOfWork>();
        var logger = Substitute.For<ILogger<TransicionarStatusPedidoFornecedorUseCase>>();
        var useCase = new TransicionarStatusPedidoFornecedorUseCase(pedidoRepo, uow, logger);

        var empresaId = Guid.NewGuid();
        var pedidoId = Guid.NewGuid();

        pedidoRepo.GetByIdAsync(pedidoId).Returns(new PedidoFornecedor
        {
            Id = pedidoId,
            EmpresaId = empresaId,
            Status = StatusPedidoFornecedor.Aberto,
            DataPedido = DateTime.UtcNow
        });

        await useCase.ExecuteAsync(new TransicionarStatusPedidoFornecedorCommand(
            pedidoId, empresaId, StatusPedidoFornecedor.EmTransito, "TRACK001"));

        await pedidoRepo.Received(1).UpdateAsync(Arg.Is<PedidoFornecedor>(p =>
            p.Status == StatusPedidoFornecedor.EmTransito &&
            p.Tracking == "TRACK001"));
        await uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task Deve_rejeitar_transicao_para_Recebido_via_status_endpoint()
    {
        var pedidoRepo = Substitute.For<IPedidoFornecedorRepository>();
        var uow = Substitute.For<IUnitOfWork>();
        var logger = Substitute.For<ILogger<TransicionarStatusPedidoFornecedorUseCase>>();
        var useCase = new TransicionarStatusPedidoFornecedorUseCase(pedidoRepo, uow, logger);

        var act = () => useCase.ExecuteAsync(new TransicionarStatusPedidoFornecedorCommand(
            Guid.NewGuid(), Guid.NewGuid(), StatusPedidoFornecedor.Recebido));

        await act.Should().ThrowAsync<UseCaseValidationException>()
            .WithMessage("*endpoint dedicado*");
        await uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task Deve_rejeitar_transicao_invalida_via_use_case()
    {
        var pedidoRepo = Substitute.For<IPedidoFornecedorRepository>();
        var uow = Substitute.For<IUnitOfWork>();
        var logger = Substitute.For<ILogger<TransicionarStatusPedidoFornecedorUseCase>>();
        var useCase = new TransicionarStatusPedidoFornecedorUseCase(pedidoRepo, uow, logger);

        var empresaId = Guid.NewGuid();
        var pedidoId = Guid.NewGuid();

        pedidoRepo.GetByIdAsync(pedidoId).Returns(new PedidoFornecedor
        {
            Id = pedidoId,
            EmpresaId = empresaId,
            Status = StatusPedidoFornecedor.Cancelado,
            DataPedido = DateTime.UtcNow
        });

        var act = () => useCase.ExecuteAsync(new TransicionarStatusPedidoFornecedorCommand(
            pedidoId, empresaId, StatusPedidoFornecedor.EmTransito));

        await act.Should().ThrowAsync<UseCaseValidationException>();
        await uow.DidNotReceive().CommitAsync();
    }

    // ──────────────────────────────────────────────
    // Recebimento
    // ──────────────────────────────────────────────

    [Fact]
    public async Task Deve_receber_pedido_e_criar_entradas_e_notificacao()
    {
        var pedidoRepo = Substitute.For<IPedidoFornecedorRepository>();
        var fornecedorRepo = Substitute.For<IFornecedorRepository>();
        var produtoRepo = Substitute.For<IProdutoRepository>();
        var variacaoRepo = Substitute.For<IProdutoVariacaoRepository>();
        var itemEstoqueRepo = Substitute.For<IItemEstoqueRepository>();
        var movimentacaoRepo = Substitute.For<IMovimentacaoEstoqueRepository>();
        var notificacaoRepo = Substitute.For<INotificacaoRepository>();
        var uow = Substitute.For<IUnitOfWork>();
        var logger = Substitute.For<ILogger<ReceberPedidoFornecedorUseCase>>();

        var useCase = new ReceberPedidoFornecedorUseCase(
            pedidoRepo, fornecedorRepo, produtoRepo, variacaoRepo,
            itemEstoqueRepo, movimentacaoRepo, notificacaoRepo, uow, logger);

        var empresaId = Guid.NewGuid();
        var fornecedorId = Guid.NewGuid();
        var pedidoId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();

        var produto = new Produto
        {
            Id = produtoId,
            EmpresaId = empresaId,
            Nome = "Produto X",
            Status = StatusProduto.Ativo,
            SkuBase = null,
            Dimensoes = null,
            PrecoReferencia = null
        };

        var itens = new List<ItemPedidoFornecedor>
        {
            ItemPedidoFornecedor.Criar(pedidoId, empresaId, produtoId, null, "Produto X", 10, 25m),
            ItemPedidoFornecedor.Criar(pedidoId, empresaId, null, null, "Item sem produto", 5, null)
        };

        var pedido = new PedidoFornecedor
        {
            Id = pedidoId,
            EmpresaId = empresaId,
            FornecedorId = fornecedorId,
            Status = StatusPedidoFornecedor.EmTransito,
            DataPedido = DateTime.UtcNow.AddDays(-5),
            Itens = itens
        };

        var fornecedor = new Fornecedor
        {
            Id = fornecedorId,
            EmpresaId = empresaId,
            Nome = "Fornecedor Teste",
            Ativo = true,
            LeadTimeRealMedioDias = null
        };

        pedidoRepo.GetByIdComItensAsync(pedidoId).Returns(pedido);
        fornecedorRepo.GetByIdAsync(empresaId, fornecedorId).Returns(fornecedor);
        produtoRepo.GetByIdAsync(produtoId).Returns(produto);

        var result = await useCase.ExecuteAsync(new ReceberPedidoFornecedorCommand(pedidoId, empresaId));

        result.Status.Should().Be(StatusPedidoFornecedor.Recebido);
        result.DataRecebimento.Should().NotBeNull();

        // Deve criar ItemEstoque e Movimentacao apenas para o item com ProdutoId
        await itemEstoqueRepo.Received(1).InsertAsync(Arg.Any<ItemEstoque>());
        await movimentacaoRepo.Received(1).InsertAsync(Arg.Any<MovimentacaoEstoque>());

        // Deve criar notificação de recebimento
        await notificacaoRepo.Received(1).AddAsync(Arg.Is<Notificacao>(n =>
            n.TipoAlerta == TipoAlertaEstoque.PedidoRecebido &&
            n.EmpresaId == empresaId &&
            n.ReferenciaId == pedidoId));

        // Deve atualizar lead time do fornecedor
        await fornecedorRepo.Received(1).UpdateAsync(Arg.Is<Fornecedor>(f =>
            f.LeadTimeRealMedioDias.HasValue));

        await uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task Deve_rejeitar_recebimento_de_pedido_ja_recebido()
    {
        var pedidoRepo = Substitute.For<IPedidoFornecedorRepository>();
        var fornecedorRepo = Substitute.For<IFornecedorRepository>();
        var produtoRepo = Substitute.For<IProdutoRepository>();
        var variacaoRepo = Substitute.For<IProdutoVariacaoRepository>();
        var itemEstoqueRepo = Substitute.For<IItemEstoqueRepository>();
        var movimentacaoRepo = Substitute.For<IMovimentacaoEstoqueRepository>();
        var notificacaoRepo = Substitute.For<INotificacaoRepository>();
        var uow = Substitute.For<IUnitOfWork>();
        var logger = Substitute.For<ILogger<ReceberPedidoFornecedorUseCase>>();

        var useCase = new ReceberPedidoFornecedorUseCase(
            pedidoRepo, fornecedorRepo, produtoRepo, variacaoRepo,
            itemEstoqueRepo, movimentacaoRepo, notificacaoRepo, uow, logger);

        var empresaId = Guid.NewGuid();
        var pedidoId = Guid.NewGuid();

        pedidoRepo.GetByIdComItensAsync(pedidoId).Returns(new PedidoFornecedor
        {
            Id = pedidoId,
            EmpresaId = empresaId,
            FornecedorId = Guid.NewGuid(),
            Status = StatusPedidoFornecedor.Recebido,
            DataPedido = DateTime.UtcNow.AddDays(-10),
            DataRecebimento = DateTime.UtcNow.AddDays(-1),
            Itens = new List<ItemPedidoFornecedor>()
        });

        var act = () => useCase.ExecuteAsync(new ReceberPedidoFornecedorCommand(pedidoId, empresaId));

        await act.Should().ThrowAsync<UseCaseValidationException>()
            .WithMessage("*invalida*");
        await uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task Deve_rejeitar_recebimento_de_pedido_cancelado()
    {
        var pedidoRepo = Substitute.For<IPedidoFornecedorRepository>();
        var fornecedorRepo = Substitute.For<IFornecedorRepository>();
        var produtoRepo = Substitute.For<IProdutoRepository>();
        var variacaoRepo = Substitute.For<IProdutoVariacaoRepository>();
        var itemEstoqueRepo = Substitute.For<IItemEstoqueRepository>();
        var movimentacaoRepo = Substitute.For<IMovimentacaoEstoqueRepository>();
        var notificacaoRepo = Substitute.For<INotificacaoRepository>();
        var uow = Substitute.For<IUnitOfWork>();
        var logger = Substitute.For<ILogger<ReceberPedidoFornecedorUseCase>>();

        var useCase = new ReceberPedidoFornecedorUseCase(
            pedidoRepo, fornecedorRepo, produtoRepo, variacaoRepo,
            itemEstoqueRepo, movimentacaoRepo, notificacaoRepo, uow, logger);

        var empresaId = Guid.NewGuid();
        var pedidoId = Guid.NewGuid();

        pedidoRepo.GetByIdComItensAsync(pedidoId).Returns(new PedidoFornecedor
        {
            Id = pedidoId,
            EmpresaId = empresaId,
            FornecedorId = Guid.NewGuid(),
            Status = StatusPedidoFornecedor.Cancelado,
            DataPedido = DateTime.UtcNow.AddDays(-3),
            Itens = new List<ItemPedidoFornecedor>()
        });

        var act = () => useCase.ExecuteAsync(new ReceberPedidoFornecedorCommand(pedidoId, empresaId));

        await act.Should().ThrowAsync<UseCaseValidationException>();
        await uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task Receber_deve_atualizar_lead_time_real_baseado_na_media()
    {
        var pedidoRepo = Substitute.For<IPedidoFornecedorRepository>();
        var fornecedorRepo = Substitute.For<IFornecedorRepository>();
        var produtoRepo = Substitute.For<IProdutoRepository>();
        var variacaoRepo = Substitute.For<IProdutoVariacaoRepository>();
        var itemEstoqueRepo = Substitute.For<IItemEstoqueRepository>();
        var movimentacaoRepo = Substitute.For<IMovimentacaoEstoqueRepository>();
        var notificacaoRepo = Substitute.For<INotificacaoRepository>();
        var uow = Substitute.For<IUnitOfWork>();
        var logger = Substitute.For<ILogger<ReceberPedidoFornecedorUseCase>>();

        var useCase = new ReceberPedidoFornecedorUseCase(
            pedidoRepo, fornecedorRepo, produtoRepo, variacaoRepo,
            itemEstoqueRepo, movimentacaoRepo, notificacaoRepo, uow, logger);

        var empresaId = Guid.NewGuid();
        var fornecedorId = Guid.NewGuid();
        var pedidoId = Guid.NewGuid();

        // Pedido criado há 10 dias – lead time esperado = 10 dias
        var pedido = new PedidoFornecedor
        {
            Id = pedidoId,
            EmpresaId = empresaId,
            FornecedorId = fornecedorId,
            Status = StatusPedidoFornecedor.EmTransito,
            DataPedido = DateTime.UtcNow.AddDays(-10),
            Itens = new List<ItemPedidoFornecedor>()
        };

        // Fornecedor já possui lead time real médio de 8 dias
        var fornecedor = new Fornecedor
        {
            Id = fornecedorId,
            EmpresaId = empresaId,
            Nome = "Fornecedor LT",
            Ativo = true,
            LeadTimeRealMedioDias = 8m
        };

        pedidoRepo.GetByIdComItensAsync(pedidoId).Returns(pedido);
        fornecedorRepo.GetByIdAsync(empresaId, fornecedorId).Returns(fornecedor);

        await useCase.ExecuteAsync(new ReceberPedidoFornecedorCommand(pedidoId, empresaId));

        // Nova média = (8 + 10) / 2 = 9
        await fornecedorRepo.Received(1).UpdateAsync(Arg.Is<Fornecedor>(f =>
            f.LeadTimeRealMedioDias == 9m));
    }
}
