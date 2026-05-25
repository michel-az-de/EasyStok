using EasyStock.Application.Ports.Output.Integration;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.CriarSugestaoCompra;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.UseCases.CriarSugestaoCompra;

public class CriarSugestaoCompraUseCaseTests
{
    private readonly IPedidoFornecedorRepository _pedidoRepo = Substitute.For<IPedidoFornecedorRepository>();
    private readonly IFornecedorRepository _fornecedorRepo = Substitute.For<IFornecedorRepository>();
    private readonly IPublicadorEventoIntegracao _publicador = Substitute.For<IPublicadorEventoIntegracao>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly ILogger<CriarSugestaoCompraUseCase> _logger = Substitute.For<ILogger<CriarSugestaoCompraUseCase>>();

    private CriarSugestaoCompraUseCase Build()
    {
        _uow.ExecuteInTransactionAsync(Arg.Any<Func<CancellationToken, Task>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var action = callInfo.Arg<Func<CancellationToken, Task>>();
                return action(CancellationToken.None);
            });
        return new CriarSugestaoCompraUseCase(_pedidoRepo, _fornecedorRepo, _publicador, _uow, _logger);
    }

    private CriarSugestaoCompraCommand BuildCommand(Guid empresaId, Guid fornecedorId, string idempotencyKey = "test-key")
        => new(
            empresaId, null,
            [new FornecedorGrupoInput(fornecedorId,
                [new ItemFaltanteInput(Guid.NewGuid(), "Farinha", 2m, UnidadeMedida.Kg, 5m, null)])],
            "calculadora", null, idempotencyKey);

    [Fact]
    public async Task Idempotency_key_vazio_lanca()
    {
        var uc = Build();
        var ex = await Assert.ThrowsAsync<UseCaseValidationException>(() => uc.ExecuteAsync(
            new CriarSugestaoCompraCommand(Guid.NewGuid(), null, [], null, null, "")));
        ex.Code.Should().Be("INVALID_IDEMPOTENCY_KEY");
    }

    [Fact]
    public async Task Lista_fornecedores_vazia_lanca_EMPTY_REQUEST()
    {
        var uc = Build();
        var ex = await Assert.ThrowsAsync<UseCaseValidationException>(() => uc.ExecuteAsync(
            new CriarSugestaoCompraCommand(Guid.NewGuid(), null, [], null, null, "uuid-123")));
        ex.Code.Should().Be("EMPTY_REQUEST");
    }

    [Fact]
    public async Task Fornecedor_inativo_lanca_SUPPLIER_INACTIVE_e_nada_e_criado()
    {
        var empresaId = Guid.NewGuid();
        var fornecedorId = Guid.NewGuid();
        var fornecedor = Fornecedor.Criar(empresaId, "Fornecedor X");
        fornecedor.Id = fornecedorId;
        fornecedor.Desativar();

        _fornecedorRepo.GetByIdAsync(empresaId, fornecedorId).Returns(fornecedor);

        var uc = Build();
        var ex = await Assert.ThrowsAsync<UseCaseValidationException>(() => uc.ExecuteAsync(
            BuildCommand(empresaId, fornecedorId)));

        ex.Code.Should().Be("SUPPLIER_INACTIVE");

        // Confirma que nada foi inserido (pre-validacao falhou ANTES de comecar transacao)
        await _pedidoRepo.DidNotReceive().AddAsync(Arg.Any<PedidoFornecedor>());
        await _publicador.DidNotReceive().PublicarAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<object>(),
            Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Fornecedor_inexistente_lanca_SUPPLIER_REQUIRED()
    {
        var empresaId = Guid.NewGuid();
        var fornecedorId = Guid.NewGuid();
        _fornecedorRepo.GetByIdAsync(empresaId, fornecedorId).Returns((Fornecedor?)null);

        var uc = Build();
        var ex = await Assert.ThrowsAsync<UseCaseValidationException>(() => uc.ExecuteAsync(
            BuildCommand(empresaId, fornecedorId)));

        ex.Code.Should().Be("SUPPLIER_REQUIRED");
    }

    [Fact]
    public async Task Sucesso_com_1_fornecedor_cria_1_PF_e_publica_1_outbox()
    {
        var empresaId = Guid.NewGuid();
        var fornecedor = Fornecedor.Criar(empresaId, "Acougue do Joao");
        _fornecedorRepo.GetByIdAsync(empresaId, fornecedor.Id).Returns(fornecedor);

        var uc = Build();
        var result = await uc.ExecuteAsync(BuildCommand(empresaId, fornecedor.Id, "uuid-abc"));

        result.PedidosCriados.Should().HaveCount(1);
        result.PedidosCriados[0].FornecedorNome.Should().Be("Acougue do Joao");

        await _pedidoRepo.Received(1).AddAsync(Arg.Any<PedidoFornecedor>());
        await _pedidoRepo.Received(1).AddItemAsync(Arg.Any<PedidoFornecedorItem>());
        await _publicador.Received(1).PublicarAsync(
            empresaId,
            "pedido_fornecedor.criado_via_calculadora",
            "PedidoFornecedor",
            Arg.Any<Guid>(),
            Arg.Any<object>(),
            Arg.Any<int>(),
            "uuid-abc",
            Arg.Any<Guid?>(),
            Arg.Any<CancellationToken>());
    }
}
