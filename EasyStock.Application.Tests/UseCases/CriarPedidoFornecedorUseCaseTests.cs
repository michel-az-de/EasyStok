using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Pedido;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.UseCases;

public class CriarPedidoFornecedorUseCaseTests
{
    private readonly IPedidoFornecedorRepository _pedidoRepo = Substitute.For<IPedidoFornecedorRepository>();
    private readonly IFornecedorRepository _fornecedorRepo = Substitute.For<IFornecedorRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly Guid _empresaId = Guid.NewGuid();

    private CriarPedidoFornecedorUseCase Build() => new(
        _pedidoRepo, _fornecedorRepo, _uow, Substitute.For<ILogger<CriarPedidoFornecedorUseCase>>());

    private CriarPedidoFornecedorCommand Comando(Guid fornecedorId) => new(
        _empresaId, fornecedorId, new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc), null, null, null, null);

    [Fact]
    public async Task Fornecedor_inativo_lanca_SUPPLIER_INACTIVE_e_nada_e_gravado()
    {
        var fornecedor = Fornecedor.Criar(_empresaId, "Moinho");
        fornecedor.Desativar();
        _fornecedorRepo.GetByIdAsync(_empresaId, fornecedor.Id).Returns(fornecedor);

        var ex = await Assert.ThrowsAsync<UseCaseValidationException>(
            () => Build().ExecuteAsync(Comando(fornecedor.Id)));

        ex.Code.Should().Be("SUPPLIER_INACTIVE");
        await _pedidoRepo.DidNotReceive().AddAsync(Arg.Any<PedidoFornecedor>());
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task Fornecedor_ativo_cria_o_pedido_aberto()
    {
        var fornecedor = Fornecedor.Criar(_empresaId, "Moinho");
        _fornecedorRepo.GetByIdAsync(_empresaId, fornecedor.Id).Returns(fornecedor);

        var result = await Build().ExecuteAsync(Comando(fornecedor.Id));

        result.PedidoId.Should().NotBeEmpty();
        await _pedidoRepo.Received(1).AddAsync(Arg.Is<PedidoFornecedor>(p =>
            p.FornecedorId == fornecedor.Id && p.Status == StatusPedidoFornecedor.Aberto));
        await _uow.Received(1).CommitAsync();
    }
}
