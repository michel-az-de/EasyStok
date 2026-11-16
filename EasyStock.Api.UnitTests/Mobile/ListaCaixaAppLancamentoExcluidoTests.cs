using EasyStock.Api.Mobile.Controllers;
using EasyStock.Application.Ports.Output;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Mobile;

/// <summary>
/// #2730: lançamento de caixa excluído no PWA (exclusão lógica por DeletedAt) não aparece na lista de
/// lançamentos do app (tela Caixa mobile do Web). O tombstone do sync sai pelo SyncController, não por aqui.
/// </summary>
public sealed class ListaCaixaAppLancamentoExcluidoTests : IDisposable
{
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly EasyStockDbContext _db;

    public ListaCaixaAppLancamentoExcluidoTests()
    {
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.EmpresaId.Returns(_empresaId);
        _db = new EasyStockDbContext(
            new DbContextOptionsBuilder<EasyStockDbContext>()
                .UseInMemoryDatabase($"lista-caixa-app-{Guid.NewGuid()}").Options,
            _currentUser);
    }

    public void Dispose() => _db.Dispose();

    private void Lancamento(string id, bool excluido) => _db.Add(new CashEntry
    {
        Id = id, EmpresaId = _empresaId, Type = "income", Amount = 10m, Description = "teste",
        CreatedAt = DateTime.UtcNow, DeletedAt = excluido ? DateTime.UtcNow : null,
    });

    private async Task<MobileCashSummary[]> Listar(bool? pendingOnly)
    {
        // List não usa o caso de uso de registrar movimento.
        var controller = new MobileCashController(_db, null!, _currentUser, NullLogger<MobileCashController>.Instance);
        return (MobileCashSummary[])((OkObjectResult)await controller.List(_empresaId, pendingOnly, default)).Value!;
    }

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Lancamento_excluido_no_PWA_nao_aparece_na_lista_do_app(bool? pendingOnly)
    {
        Lancamento("ativo", excluido: false);
        Lancamento("excluido", excluido: true);
        await _db.SaveChangesAsync();

        var itens = await Listar(pendingOnly);

        itens.Select(i => i.Id).Should().Equal("ativo");
    }
}
