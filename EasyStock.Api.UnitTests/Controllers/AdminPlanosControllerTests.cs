using EasyStock.Api.Controllers;
using EasyStock.Api.Services;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Controllers;

/// <summary>
/// QA 2026-07-09 BUG-08 (#891): dois planos "Starter" ativos eram aceitos, e o modal "Trocar plano"
/// mostrava duas opcoes identicas. Cupom ja tinha o par check-de-aplicacao (409) + indice unico;
/// Plano nao tinha nenhum dos dois. Estes testes cobrem o check de aplicacao (controller) e a
/// semantica case/trim do repositorio. O backstop de banco (uq_planos_nome_lower) vive na migration
/// AddUniquePlanoNomeInsensivel e nao e exercitado aqui (InMemory nao aplica constraint).
/// </summary>
public class AdminPlanosControllerTests : IDisposable
{
    private readonly IPlanoAdminRepository _planos = Substitute.For<IPlanoAdminRepository>();
    private readonly IEmpresaRepository _empresas = Substitute.For<IEmpresaRepository>();
    private readonly EasyStockDbContext _db;
    private readonly AdminPlanosController _controller;

    private static readonly Guid _planoId = Guid.NewGuid();

    public AdminPlanosControllerTests()
    {
        _db = new EasyStockDbContext(new DbContextOptionsBuilder<EasyStockDbContext>()
            .UseInMemoryDatabase($"admin-planos-tests-{Guid.NewGuid()}")
            .Options);

        var http = Substitute.For<IHttpContextAccessor>();
        http.HttpContext.Returns((HttpContext?)null);

        _empresas.GetAllAsync().Returns(Enumerable.Empty<Empresa>());

        _controller = new AdminPlanosController(
            _planos, _empresas, new AdminAuditService(_db, http, NullLogger<AdminAuditService>.Instance));
    }

    private static CreatePlanoRequest Novo(string nome) => new(nome, null, 1, 5, 100, 10, 49.90m);

    [Fact]
    public async Task CreatePlano_ComNomeJaExistente_Retorna409()
    {
        _planos.ExisteNomeAsync("Starter", null, Arg.Any<CancellationToken>()).Returns(true);

        var resultado = await _controller.CreatePlano(Novo("Starter"));

        resultado.Should().BeOfType<ConflictObjectResult>();
        await _planos.DidNotReceive().CriarAsync(Arg.Any<NovoPlano>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreatePlano_ComNomeInedito_Persiste()
    {
        _planos.ExisteNomeAsync(Arg.Any<string>(), null, Arg.Any<CancellationToken>()).Returns(false);
        _planos.CriarAsync(Arg.Any<NovoPlano>(), Arg.Any<CancellationToken>())
            .Returns(new PlanoResumo(_planoId, "Pro"));

        var resultado = await _controller.CreatePlano(Novo("Pro"));

        resultado.Should().NotBeOfType<ConflictObjectResult>();
        await _planos.Received(1).CriarAsync(Arg.Any<NovoPlano>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PatchPlano_RenomeandoParaNomeExistente_Retorna409()
    {
        _planos.ExisteNomeAsync("Starter", _planoId, Arg.Any<CancellationToken>()).Returns(true);

        var resultado = await _controller.PatchPlano(_planoId,
            new PatchPlanoAdminRequest("Starter", null, 1, 5, 100, 10, 49.90m));

        resultado.Should().BeOfType<ConflictObjectResult>();
        await _planos.DidNotReceive().AtualizarAsync(
            Arg.Any<Guid>(), Arg.Any<PatchPlano>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PatchPlano_PassaOProprioIdComoIgnorarId()
    {
        // Sem o ignorarId, salvar a edicao sem mudar o nome colidiria com o proprio plano — o
        // Admin sempre reenvia o conjunto completo de campos (Pages/Planos/Index.cshtml.cs:67).
        _planos.ExisteNomeAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(false);
        _planos.AtualizarAsync(Arg.Any<Guid>(), Arg.Any<PatchPlano>(), Arg.Any<CancellationToken>())
            .Returns(new PlanoResumo(_planoId, "Starter"));

        await _controller.PatchPlano(_planoId, new PatchPlanoAdminRequest("Starter", null, 1, 5, 100, 10, 49.90m));

        await _planos.Received(1).ExisteNomeAsync("Starter", _planoId, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("Starter")]      // igual
    [InlineData("starter")]      // caixa diferente
    [InlineData("STARTER")]
    [InlineData("  starter  ")]  // espacos nas bordas
    public async Task ExisteNomeAsync_IgnoraCaixaEEspacos(string candidato)
    {
        var repo = SemeiaRepositorioCom("Starter");

        (await repo.ExisteNomeAsync(candidato)).Should().BeTrue();
    }

    [Fact]
    public async Task ExisteNomeAsync_NaoAcusaNomeDiferente()
    {
        var repo = SemeiaRepositorioCom("Starter");

        (await repo.ExisteNomeAsync("Pro")).Should().BeFalse();
    }

    [Fact]
    public async Task ExisteNomeAsync_ComIgnorarId_NaoColideConsigoMesmo()
    {
        var repo = SemeiaRepositorioCom("Starter");

        (await repo.ExisteNomeAsync("Starter", _planoId)).Should().BeFalse();
    }

    private PlanoAdminRepository SemeiaRepositorioCom(string nome)
    {
        _db.Planos.Add(new Plano
        {
            Id = _planoId,
            Nome = nome,
            LimiteLojas = 1,
            LimiteUsuarios = 5,
            LimiteProdutos = 100,
            LimiteGeracoesIaMensais = 10,
            PrecoMensal = 0m,
            Ativo = true,
            CriadoEm = DateTime.UtcNow,
        });
        _db.SaveChanges();
        return new PlanoAdminRepository(_db);
    }

    public void Dispose() => _db.Dispose();
}
