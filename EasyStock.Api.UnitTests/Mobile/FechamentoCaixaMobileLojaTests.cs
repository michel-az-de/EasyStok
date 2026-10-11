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
/// #1543 (#954): o fechamento de caixa que o PWA manda é do escopo do aparelho, loja com loja e
/// empresa com empresa. O casamento frouxo sobrescrevia o fechamento da empresa com os números de uma
/// loja e, no aparelho sem loja, o de uma loja qualquer.
/// </summary>
public sealed class FechamentoCaixaMobileLojaTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly DateOnly Dia = new(2026, 10, 10);

    private readonly EasyStockDbContext _db;
    private readonly SyncMutationDispatcher _dispatcher;
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly Guid _lojaA = Guid.NewGuid();
    private readonly Guid _lojaB = Guid.NewGuid();

    public FechamentoCaixaMobileLojaTests()
    {
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.EmpresaId.Returns(_empresaId);
        _db = new EasyStockDbContext(
            new DbContextOptionsBuilder<EasyStockDbContext>().UseInMemoryDatabase($"fechamento-loja-{Guid.NewGuid()}").Options,
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

    [Fact]
    public async Task AparelhoDaLoja_NaoSobrescreveOFechamentoDaEmpresa()
    {
        var daEmpresa = SemearFechamento(lojaId: null, saldoFinal: 900m);

        await FecharAsync(_lojaA, saldoFinal: 150m);

        Fechamento(daEmpresa).SaldoFinal.Should().Be(900m, "o consolidado da empresa nao e o caixa da loja A");
        Fechamentos().Should().HaveCount(2);
        Fechamentos().Single(f => f.LojaId == _lojaA).SaldoFinal.Should().Be(150m);
    }

    [Fact]
    public async Task AparelhoSemLoja_NaoSobrescreveOFechamentoDeUmaLoja()
    {
        var daLojaB = SemearFechamento(_lojaB, saldoFinal: 320m);

        await FecharAsync(lojaId: null, saldoFinal: 70m);

        Fechamento(daLojaB).SaldoFinal.Should().Be(320m);
        Fechamentos().Single(f => f.LojaId == null).SaldoFinal.Should().Be(70m);
    }

    [Fact]
    public async Task AparelhoDeUmaLoja_NaoSobrescreveOFechamentoDeOutra()
    {
        var daLojaB = SemearFechamento(_lojaB, saldoFinal: 320m);

        await FecharAsync(_lojaA, saldoFinal: 150m);

        Fechamento(daLojaB).SaldoFinal.Should().Be(320m);
        Fechamentos().Should().HaveCount(2);
    }

    [Fact]
    public async Task MesmoEscopoFechandoDeNovo_AtualizaOProprioFechamento()
    {
        await FecharAsync(_lojaA, saldoFinal: 150m);

        await FecharAsync(_lojaA, saldoFinal: 180m);

        Fechamentos().Should().ContainSingle().Which.SaldoFinal.Should().Be(180m);
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
