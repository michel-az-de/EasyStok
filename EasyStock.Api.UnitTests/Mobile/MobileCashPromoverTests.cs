using EasyStock.Api.Mobile.Controllers;
using EasyStock.Api.Mobile.Services.Linkers;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.UseCases.Caixa;
using EasyStock.Application.UseCases.ObterCaixaDia;
using EasyStock.Application.UseCases.RegistrarMovimentoCaixa;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Mobile;

/// <summary>
/// #1615: "desvincular" e "promover" de novo criava um segundo movimento ativo para o mesmo
/// lancamento do PWA, e o valor entrava duas vezes no caixa. A promocao manual passa a usar a mesma
/// referencia do vinculo automatico (<see cref="CashEntryLinker"/>).
/// </summary>
public sealed class MobileCashPromoverTests : IDisposable
{
    private const string Lancamento = "cash-1";

    private readonly EasyStockDbContext _db;
    private readonly MobileCashController _controller;
    private readonly Guid _empresaId = Guid.NewGuid();

    public MobileCashPromoverTests()
    {
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.EmpresaId.Returns(_empresaId);
        currentUser.Nivel.Returns(NivelAcesso.Gerente);

        _db = new EasyStockDbContext(
            new DbContextOptionsBuilder<EasyStockDbContext>()
                .UseInMemoryDatabase($"caixa-promover-{Guid.NewGuid()}")
                .Options,
            currentUser);

        var repo = new CaixaRepository(_db);
        var registrar = new RegistrarMovimentoCaixaUseCase(
            repo, _db,
            new ObterCaixaDiaUseCase(new CaixaSaldoCalculator(repo)),
            NullLogger<RegistrarMovimentoCaixaUseCase>.Instance);

        _controller = new MobileCashController(_db, registrar, currentUser, NullLogger<MobileCashController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Promover_DepoisDeDesvincular_ReligaOMesmoMovimento()
    {
        Semear();
        await _controller.Promover(Lancamento, null, default);
        var primeiro = Movimentos().Should().ContainSingle().Subject;
        primeiro.Referencia.Should().Be("mobile:cash-1");

        await _controller.Unlink(Lancamento, null, default);
        var resposta = await _controller.Promover(Lancamento, null, default);

        resposta.Should().BeOfType<OkObjectResult>();
        Movimentos().Should().ContainSingle("o lancamento ja tem movimento no caixa");
        _db.Set<CashEntry>().AsNoTracking().Single().ErpMovimentoCaixaId.Should().Be(primeiro.Id);
    }

    [Fact]
    public async Task Promover_LancamentoExcluidoNoPwa_Recusa()
    {
        Semear(excluido: true);

        var resposta = await _controller.Promover(Lancamento, null, default);

        resposta.Should().BeOfType<BadRequestObjectResult>();
        Movimentos().Should().BeEmpty("lancamento excluido nao entra no caixa do ERP");
    }

    private void Semear(bool excluido = false)
    {
        _db.Add(new CashEntry
        {
            Id = Lancamento, Type = "income", Amount = 30m, Description = "Troco",
            CreatedAt = DateTime.UtcNow.AddHours(-1), EmpresaId = _empresaId, Metodo = "dinheiro",
            DeletedAt = excluido ? DateTime.UtcNow : null
        });
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
    }

    private List<MovimentoCaixa> Movimentos() =>
        _db.Set<MovimentoCaixa>().IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.EmpresaId == _empresaId).ToList();
}
