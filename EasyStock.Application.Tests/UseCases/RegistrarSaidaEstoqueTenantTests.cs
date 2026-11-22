using EasyStock.Application.Ports.Output.Events;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.RegistrarSaidaEstoque;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.UseCases;

/// <summary>#3099: loja e vendedor do corpo precisam pertencer à empresa do JWT.</summary>
public class RegistrarSaidaEstoqueTenantTests
{
    private readonly ILojaRepository _lojas = Substitute.For<ILojaRepository>();
    private readonly IUsuarioEmpresaRepository _usuariosEmpresa = Substitute.For<IUsuarioEmpresaRepository>();
    private readonly IItemEstoqueRepository _itens = Substitute.For<IItemEstoqueRepository>();

    private RegistrarSaidaEstoqueUseCase BuildUseCase(bool semRepositorios = false)
    {
        var unitOfWork = Substitute.For<IUnitOfWork>();
        unitOfWork.SetupExecuteInTransactionSemRetryForward<RegistrarSaidaEstoqueResult>();
        return new(
            Substitute.For<IProdutoRepository>(),
            _itens,
            Substitute.For<IVendaRepository>(),
            Substitute.For<IItemVendaRepository>(),
            Substitute.For<IMovimentacaoEstoqueRepository>(),
            unitOfWork,
            Substitute.For<ILogger<RegistrarSaidaEstoqueUseCase>>(),
            Substitute.For<IPublicadorEventos>(),
            null,
            null,
            semRepositorios ? null : _lojas,
            semRepositorios ? null : _usuariosEmpresa);
    }

    private static RegistrarSaidaEstoqueCommand Command(Guid empresaId, Guid? lojaId, Guid? vendedorId) =>
        new(empresaId,
            [new RegistrarSaidaEstoqueItemCommand(Guid.NewGuid(), 1, 100m, "x")],
            DateTime.UtcNow, DateTime.UtcNow, null, null,
            NaturezaMovimentacaoEstoque.Venda, CanalVenda.LojaPropria, null,
            VendedorId: vendedorId, LojaId: lojaId);

    [Fact]
    public async Task Loja_de_outra_empresa_e_recusada()
    {
        var empresaId = Guid.NewGuid();
        var lojaAlheia = Guid.NewGuid();
        _lojas.GetByIdAsync(empresaId, lojaAlheia).Returns((Loja?)null);

        var act = () => BuildUseCase().ExecuteAsync(Command(empresaId, lojaAlheia, null));

        (await act.Should().ThrowAsync<UseCaseValidationException>()).WithMessage("*loja*");
    }

    [Fact]
    public async Task Vendedor_de_outra_empresa_e_recusado()
    {
        var empresaId = Guid.NewGuid();
        var vendedorAlheio = Guid.NewGuid();
        _usuariosEmpresa.GetByUsuarioEEmpresaAsync(vendedorAlheio, empresaId).Returns((UsuarioEmpresa?)null);

        var act = () => BuildUseCase().ExecuteAsync(Command(empresaId, null, vendedorAlheio));

        (await act.Should().ThrowAsync<UseCaseValidationException>()).WithMessage("*vendedor*");
    }

    [Fact]
    public async Task Loja_e_vendedor_da_propria_empresa_seguem_adiante()
    {
        var empresaId = Guid.NewGuid();
        var lojaId = Guid.NewGuid();
        var vendedorId = Guid.NewGuid();
        _lojas.GetByIdAsync(empresaId, lojaId).Returns(Loja.Criar(empresaId, "Matriz"));
        _usuariosEmpresa.GetByUsuarioEEmpresaAsync(vendedorId, empresaId)
            .Returns(new UsuarioEmpresa { UsuarioId = vendedorId, EmpresaId = empresaId, Ativo = true });

        var act = () => BuildUseCase().ExecuteAsync(Command(empresaId, lojaId, vendedorId));

        // Passa pelos guards de tenant e falha só adiante: o item de estoque não existe.
        (await act.Should().ThrowAsync<UseCaseValidationException>())
            .Where(e => !e.Message.Contains("loja") && !e.Message.Contains("vendedor"));
        await _lojas.Received(1).GetByIdAsync(empresaId, lojaId);
        await _usuariosEmpresa.Received(1).GetByUsuarioEEmpresaAsync(vendedorId, empresaId);
    }

    [Fact]
    public async Task Vendedor_com_vinculo_inativo_na_mesma_empresa_e_aceito()
    {
        var empresaId = Guid.NewGuid();
        var vendedorId = Guid.NewGuid();
        _usuariosEmpresa.GetByUsuarioEEmpresaAsync(vendedorId, empresaId)
            .Returns(new UsuarioEmpresa { UsuarioId = vendedorId, EmpresaId = empresaId, Ativo = false });

        var act = () => BuildUseCase().ExecuteAsync(Command(empresaId, null, vendedorId));

        (await act.Should().ThrowAsync<UseCaseValidationException>())
            .Where(e => !e.Message.Contains("vendedor"));
    }

    [Fact]
    public async Task Loja_informada_sem_repositorio_falha_fechado()
    {
        var act = () => BuildUseCase(semRepositorios: true).ExecuteAsync(Command(Guid.NewGuid(), Guid.NewGuid(), null));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*ILojaRepository*");
    }

    [Fact]
    public async Task Vendedor_informado_sem_repositorio_falha_fechado()
    {
        var act = () => BuildUseCase(semRepositorios: true).ExecuteAsync(Command(Guid.NewGuid(), null, Guid.NewGuid()));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*IUsuarioEmpresaRepository*");
    }
}
