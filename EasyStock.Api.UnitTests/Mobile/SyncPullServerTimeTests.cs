using EasyStock.Api.Mobile.Controllers;
using EasyStock.Api.Mobile.DTOs;
using EasyStock.Api.Mobile.Security;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Mobile;

/// <summary>
/// Regressao #1474 (W2): o pull calculava o <c>ServerTime</c> DEPOIS das consultas. Uma linha
/// gravada durante a consulta, com UpdatedAt menor que esse instante, ficava atras do cursor do
/// aparelho e nunca mais vinha. O instante tem de ser capturado ANTES da primeira consulta; o
/// upsert do PWA e idempotente, entao reprocessar alguns registros nao faz mal.
/// </summary>
public sealed class SyncPullServerTimeTests : IDisposable
{
    private readonly EasyStockDbContext _db;
    private readonly Guid _empresaId = Guid.NewGuid();

    public SyncPullServerTimeTests()
    {
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.EmpresaId.Returns(_empresaId);

        _db = new EasyStockDbContext(
            new DbContextOptionsBuilder<EasyStockDbContext>()
                .UseInMemoryDatabase($"sync-pull-time-{Guid.NewGuid()}")
                .Options,
            currentUser);
    }

    /// <summary>
    /// Relogio que anota quantas entidades o contexto ja tinha carregado no instante em que foi
    /// lido. Zero = lido antes de qualquer consulta do pull.
    /// </summary>
    private sealed class RelogioEspiao(EasyStockDbContext db, DateTimeOffset agora) : TimeProvider
    {
        public List<int> EntidadesCarregadasNaLeitura { get; } = [];

        public override DateTimeOffset GetUtcNow()
        {
            EntidadesCarregadasNaLeitura.Add(db.ChangeTracker.Entries().Count());
            return agora;
        }
    }

    [Fact]
    public async Task Pull_CapturaServerTimeAntesDasConsultas()
    {
        _db.Set<Product>().Add(new Product
        {
            Id = "p-1",
            Name = "Lasanha",
            UpdatedAt = DateTime.UtcNow.AddMinutes(-1),
            LastDeviceId = "outro-aparelho",
            EmpresaId = _empresaId
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var agora = DateTimeOffset.UtcNow;
        var relogio = new RelogioEspiao(_db, agora);
        // #1509: o pull exige aparelho pareado (filtra pela empresa dele).
        var http = new DefaultHttpContext();
        http.Items[MobileAuth.HttpContextItemDevice] = new MobileDevice
        {
            Id = "este-aparelho", ApiKeyHash = "h", EmpresaId = _empresaId, LojaId = Guid.Empty
        };
        var controller = new SyncController(
            _db, null!, null!, null!, null!,
            Substitute.For<IProdutoRepository>(),
            new ConfigurationBuilder().Build(),
            relogio,
            new EasyStock.Api.Mobile.Services.NumeroCurtoDosLotesMobile(_db, NSubstitute.Substitute.For<EasyStock.Application.Ports.Output.Persistence.ILoteNumeroCurtoRepository>(), NullLogger<EasyStock.Api.Mobile.Services.NumeroCurtoDosLotesMobile>.Instance),
            NullLogger<SyncController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = http }
        };

        var resultado = await controller.Pull(0, "este-aparelho");

        var corpo = (resultado.Result as OkObjectResult)!.Value.Should().BeOfType<SyncPullResponse>().Subject;
        corpo.Mutations.Should().ContainSingle(m => m.Type == "product.upsert");
        corpo.ServerTime.Should().Be(agora.ToUnixTimeMilliseconds());
        relogio.EntidadesCarregadasNaLeitura.Should().Equal([0],
            "o instante do cursor e lido uma vez, antes de qualquer consulta");
    }

    /// <summary>
    /// #2477: o carimbo ServerUpdatedAt nasce no SavingChanges, antes do commit do lote do push. Um pull que roda
    /// nesse intervalo devolve cursor maior que o carimbo de uma linha que ele ainda nao via. O pull seguinte
    /// precisa reler essa faixa, senao a linha nunca chega ao aparelho.
    /// </summary>
    [Fact]
    public async Task Pull_ReleLinhaCarimbadaPoucoAntesDoCursor()
    {
        _db.Set<Product>().Add(new Product
        {
            Id = "p-commit-tardio", Name = "Quiche", LastDeviceId = "outro-aparelho", EmpresaId = _empresaId
        });
        await _db.SaveChangesAsync();
        var carimbo = (await _db.Set<Product>().SingleAsync(p => p.Id == "p-commit-tardio")).ServerUpdatedAt;
        _db.ChangeTracker.Clear();

        // Cursor devolvido por um pull que rodou 5 s depois do carimbo, antes do commit.
        var cursor = new DateTimeOffset(DateTime.SpecifyKind(carimbo, DateTimeKind.Utc)).AddSeconds(5).ToUnixTimeMilliseconds();
        var resultado = await NovoController(new RelogioEspiao(_db, DateTimeOffset.UtcNow)).Pull(cursor, "este-aparelho");

        var corpo = (resultado.Result as OkObjectResult)!.Value.Should().BeOfType<SyncPullResponse>().Subject;
        corpo.Mutations.Should().ContainSingle(m => m.Type == "product.upsert",
            "a linha carimbada antes do cursor e commitada depois tem de voltar no pull seguinte");
    }

    private SyncController NovoController(TimeProvider relogio)
    {
        var http = new DefaultHttpContext();
        http.Items[MobileAuth.HttpContextItemDevice] = new MobileDevice
        {
            Id = "este-aparelho", ApiKeyHash = "h", EmpresaId = _empresaId, LojaId = Guid.Empty
        };
        return new SyncController(
            _db, null!, null!, null!, null!,
            Substitute.For<IProdutoRepository>(),
            new ConfigurationBuilder().Build(),
            relogio,
            new EasyStock.Api.Mobile.Services.NumeroCurtoDosLotesMobile(_db, Substitute.For<ILoteNumeroCurtoRepository>(), NullLogger<EasyStock.Api.Mobile.Services.NumeroCurtoDosLotesMobile>.Instance),
            NullLogger<SyncController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = http }
        };
    }

    public void Dispose() => _db.Dispose();
}
