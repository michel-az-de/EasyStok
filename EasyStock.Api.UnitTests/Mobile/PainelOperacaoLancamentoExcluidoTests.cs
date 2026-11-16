using EasyStock.Api.Mobile.Controllers;
using EasyStock.Api.Services.Operacao;
using EasyStock.Application.Ports.Output;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Mobile;

/// <summary>
/// #2722: lançamento de caixa excluído no PWA (exclusão lógica por DeletedAt) não entra no painel de Operação.
/// </summary>
public sealed class PainelOperacaoLancamentoExcluidoTests : IDisposable
{
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly EasyStockDbContext _db;

    public PainelOperacaoLancamentoExcluidoTests()
    {
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.EmpresaId.Returns(_empresaId);
        _db = new EasyStockDbContext(
            new DbContextOptionsBuilder<EasyStockDbContext>()
                .UseInMemoryDatabase($"painel-excluido-{Guid.NewGuid()}").Options,
            _currentUser);
    }

    public void Dispose() => _db.Dispose();

    private void Lancamento(string tipo, decimal valor, bool excluido) => _db.Add(new CashEntry
    {
        Id = Guid.NewGuid().ToString(), EmpresaId = _empresaId, Type = tipo, Amount = valor, Description = "teste",
        CreatedAt = DateTime.UtcNow, DeletedAt = excluido ? DateTime.UtcNow : null,
    });

    private async Task<OperationDashboard> Painel()
    {
        var controller = new OperationController(_db, new OperacaoEventBroker(NullLogger<OperacaoEventBroker>.Instance),
            _currentUser, Substitute.For<IConfiguration>(), NullLogger<OperationController>.Instance);
        return (OperationDashboard)((OkObjectResult)(await controller.GetDashboard(_empresaId, null, default)).Result!).Value!;
    }

    [Fact]
    public async Task Lancamento_excluido_no_PWA_nao_soma_no_caixa_do_painel()
    {
        Lancamento("income", 100m, excluido: true);
        Lancamento("expense", 40m, excluido: true);
        Lancamento("income", 30m, excluido: false);
        Lancamento("expense", 10m, excluido: false);
        await _db.SaveChangesAsync();

        var painel = await Painel();

        painel.CaixaEntradasExtras.Should().Be(30m);
        painel.CaixaSaidas.Should().Be(10m);
        painel.CaixaSaldoHoje.Should().Be(20m);
    }
}
