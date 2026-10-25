using System.Text.Json;
using EasyStock.Api.Mobile.DTOs;
using EasyStock.Api.Mobile.Services;
using EasyStock.Api.Services.Operacao;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Mobile;

/// <summary>
/// #2030: o fechamento do caixa é do console. O <c>closing.upsert</c> do sync criava ou sobrescrevia o
/// <c>FechamentoCaixa</c> com números do aparelho (saldo inicial e vendas zerados) e sem o movimento marcador;
/// depois disso a abertura do dia seguinte recusava e o caixa travava. O PWA não manda essa mutação
/// (<c>sync.js</c>), e o servidor a ignora. Antes, #1543 (#954) cuidava só do escopo loja x empresa.
/// </summary>
public sealed class FechamentoCaixaVindoDoAparelhoTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly DateOnly Dia = new(2026, 10, 10);

    private readonly EasyStockDbContext _db;
    private readonly SyncMutationDispatcher _dispatcher;
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly Guid _lojaA = Guid.NewGuid();

    public FechamentoCaixaVindoDoAparelhoTests()
    {
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.EmpresaId.Returns(_empresaId);
        _db = new EasyStockDbContext(
            new DbContextOptionsBuilder<EasyStockDbContext>().UseInMemoryDatabase($"fechamento-aparelho-{Guid.NewGuid()}").Options,
            currentUser);
        _dispatcher = new SyncMutationDispatcher(
            _db,
            new MobileStockReconciler(_db, new MobileSystemUserResolver(_db), NullLogger<MobileStockReconciler>.Instance),
            new LoteMobileEstadoReconciler(_db, NullLogger<LoteMobileEstadoReconciler>.Instance),
            new MobileSaleSyncService(_db, NullLogger<MobileSaleSyncService>.Instance),
            new OperacaoEventBroker(NullLogger<OperacaoEventBroker>.Instance),
            Substitute.For<IProdutoRepository>(),
            DispatcherDeTeste.EstornoDeCaixa(_db),
            NullLogger<SyncMutationDispatcher>.Instance);
    }

    public void Dispose() => _db.Dispose();

    [Theory]
    [InlineData(null)]
    [InlineData("loja")]
    public async Task AparelhoNaoSobrescreveOFechamentoDoConsole(string? escopo)
    {
        var loja = escopo is null ? (Guid?)null : _lojaA;
        var doConsole = SemearFechamento(loja, saldoFinal: 900m);

        await FecharAsync(loja, saldoFinal: 150m);

        Fechamentos().Should().ContainSingle().Which.Id.Should().Be(doConsole);
        Fechamento(doConsole).SaldoFinal.Should().Be(900m);
    }

    [Fact]
    public async Task AparelhoNaoCriaFechamento()
    {
        await FecharAsync(_lojaA, saldoFinal: 150m);
        await FecharAsync(lojaId: null, saldoFinal: 70m);

        Fechamentos().Should().BeEmpty("sem o movimento marcador o fechamento do aparelho travava a abertura do dia seguinte");
    }

    private async Task FecharAsync(Guid? lojaId, decimal saldoFinal)
    {
        var dto = new CashClosingDto($"close-{Guid.NewGuid():N}", Dia.ToString("yyyy-MM-dd"),
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), "Thati", saldoFinal, 0m, saldoFinal, null);
        await _dispatcher.ApplyMutationAsync(
            new MutationDto($"m-{Guid.NewGuid():N}", "dev-1", "closing.upsert", JsonSerializer.SerializeToElement(dto, Json),
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
            "dev-1", "Thati", _empresaId, lojaId);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    private Guid SemearFechamento(Guid? lojaId, decimal saldoFinal)
    {
        var f = FechamentoCaixa.Criar(_empresaId, Dia, 0, 0, saldoFinal, 0, 0, lojaId: lojaId);
        f.SaldoFinal = saldoFinal;
        _db.Add(f);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
        return f.Id;
    }

    private List<FechamentoCaixa> Fechamentos() =>
        _db.Set<FechamentoCaixa>().IgnoreQueryFilters().AsNoTracking().Where(f => f.EmpresaId == _empresaId).ToList();

    private FechamentoCaixa Fechamento(Guid id) => Fechamentos().Single(f => f.Id == id);
}
