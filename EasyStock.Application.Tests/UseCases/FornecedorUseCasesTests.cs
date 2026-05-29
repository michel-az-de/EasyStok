using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.TestHelpers;
using EasyStock.Application.UseCases.CriarFornecedor;
using EasyStock.Application.UseCases.DesativarFornecedor;
using EasyStock.Application.UseCases.Fornecedor;
using EasyStock.Application.UseCases.ListarFornecedores;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.UseCases;

public class FornecedorUseCasesTests
{
    [Fact]
    public async Task Deve_criar_fornecedor_com_campos_complementares()
    {
        var fornecedorRepository = Substitute.For<IFornecedorRepository>();
        var assinaturaRepository = Substitute.For<IAssinaturaEmpresaRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var logger = Substitute.For<ILogger<CriarFornecedorUseCase>>();
        var useCase = new CriarFornecedorUseCase(fornecedorRepository, assinaturaRepository, unitOfWork, logger);

        var empresaId = Guid.NewGuid();
        var command = new CriarFornecedorCommand(
            empresaId,
            "Fornecedor A",
            "123",
            "fornecedor@a.com",
            "199999999",
            "Ana",
            "Audio",
            "Internacional",
            18,
            "https://example.com",
            "10 unidades",
            "FOB",
            "Observacao");

        var result = await useCase.ExecuteAsync(command);

        await fornecedorRepository.Received(1).AddAsync(Arg.Is<Fornecedor>(f =>
            f.EmpresaId == empresaId &&
            f.Nome == "Fornecedor A" &&
            f.Categoria == "Audio" &&
            f.Tipo == "Internacional" &&
            f.LeadTimeEstimadoDias == 18));
        result.Categoria.Should().Be("Audio");
        await unitOfWork.Received(1).CommitAsync();
    }

    [Fact]
    public async Task Deve_bloquear_desativacao_quando_houver_pedido_aberto()
    {
        var fornecedorRepository = Substitute.For<IFornecedorRepository>();
        var pedidoRepository = Substitute.For<IPedidoFornecedorRepository>();
        var unitOfWork = new FakeUnitOfWork();
        var logger = Substitute.For<ILogger<DesativarFornecedorUseCase>>();
        var useCase = new DesativarFornecedorUseCase(fornecedorRepository, pedidoRepository, unitOfWork, logger);

        var empresaId = Guid.NewGuid();
        var fornecedorId = Guid.NewGuid();
        fornecedorRepository.GetByIdAsync(empresaId, fornecedorId).Returns(new Fornecedor
        {
            Id = fornecedorId,
            EmpresaId = empresaId,
            Nome = "Fornecedor A",
            Ativo = true
        });
        pedidoRepository.CountPedidosAbertosOuEmTransitoAsync(empresaId, fornecedorId).Returns(1);

        var act = () => useCase.ExecuteAsync(new DesativarFornecedorCommand(fornecedorId, empresaId));

        await act.Should().ThrowAsync<UseCaseValidationException>()
            .WithMessage("*pedido aberto ou em transito*");
        unitOfWork.CommitCount.Should().Be(0);
    }

    [Fact]
    public async Task Deve_retornar_estatisticas_do_fornecedor()
    {
        var fornecedorRepository = Substitute.For<IFornecedorRepository>();
        var pedidoRepository = Substitute.For<IPedidoFornecedorRepository>();
        var useCase = new ObterEstatisticasFornecedorUseCase(fornecedorRepository, pedidoRepository);

        var empresaId = Guid.NewGuid();
        var fornecedorId = Guid.NewGuid();
        fornecedorRepository.GetByIdAsync(empresaId, fornecedorId).Returns(new Fornecedor
        {
            Id = fornecedorId,
            EmpresaId = empresaId,
            Nome = "Fornecedor KPI"
        });
        pedidoRepository.GetEstatisticasAsync(empresaId, fornecedorId).Returns((4, 2500m, 12.5m, 1.33m));

        var result = await useCase.ExecuteAsync(new ObterEstatisticasFornecedorQuery(empresaId, fornecedorId));

        result.TotalGasto.Should().Be(2500m);
        result.QuantidadePedidos.Should().Be(4);
        result.LeadTimeRealMedioDias.Should().Be(12.5m);
        result.FrequenciaPedidosPorMes.Should().Be(1.33m);
    }

    [Fact]
    public async Task Deve_listar_fornecedores_com_filtro()
    {
        var fornecedorRepository = Substitute.For<IFornecedorRepository>();
        var useCase = new ListarFornecedoresUseCase(fornecedorRepository);
        var empresaId = Guid.NewGuid();
        fornecedorRepository.GetByEmpresaAsync(empresaId, 1, 20, true, "audio").Returns((
            new[]
            {
                new Fornecedor
                {
                    Id = Guid.NewGuid(),
                    EmpresaId = empresaId,
                    Nome = "Audio Supply",
                    Ativo = true
                }
            },
            1));

        var result = await useCase.ExecuteAsync(new ListarFornecedoresQuery(empresaId, 1, 20, true, "audio"));

        result.Total.Should().Be(1);
        result.Fornecedores.Should().ContainSingle();
    }

    // ── Guards (cenários absurdos) ─────────────────────────────────────────────

    [Fact]
    public async Task Deve_lancar_validation_quando_empresaId_vazio()
    {
        var fornecedorRepository = Substitute.For<IFornecedorRepository>();
        var assinaturaRepository = Substitute.For<IAssinaturaEmpresaRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var useCase = new CriarFornecedorUseCase(fornecedorRepository, assinaturaRepository, unitOfWork,
            Substitute.For<ILogger<CriarFornecedorUseCase>>());

        var act = () => useCase.ExecuteAsync(new CriarFornecedorCommand(
            Guid.Empty, "Fornecedor A", null, null, null, null));

        await act.Should().ThrowAsync<UseCaseValidationException>();
        await unitOfWork.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task Deve_lancar_validation_quando_email_invalido()
    {
        var fornecedorRepository = Substitute.For<IFornecedorRepository>();
        var assinaturaRepository = Substitute.For<IAssinaturaEmpresaRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var useCase = new CriarFornecedorUseCase(fornecedorRepository, assinaturaRepository, unitOfWork,
            Substitute.For<ILogger<CriarFornecedorUseCase>>());

        var act = () => useCase.ExecuteAsync(new CriarFornecedorCommand(
            Guid.NewGuid(), "Fornecedor A", null, "isto-nao-eh-email", null, null));

        await act.Should().ThrowAsync<UseCaseValidationException>();
        await unitOfWork.DidNotReceive().CommitAsync();
    }
}
