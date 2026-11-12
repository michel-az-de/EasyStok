using System.Text.Json;
using EasyStock.Api.Mobile.DTOs;
using EasyStock.Api.Mobile.Services;
using EasyStock.Api.Services.Operacao;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Mobile;

/// <summary>#2343 (R2-API-11): order.upsert incompleto vira rejeicao com motivo legivel, nao NullReferenceException.</summary>
public sealed class SyncOrderUpsertIncompletoTests : IDisposable
{
    private readonly EasyStockDbContext _db;
    private readonly Guid _empresaId = Guid.NewGuid();

    public SyncOrderUpsertIncompletoTests()
    {
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.EmpresaId.Returns(_empresaId);
        _db = new EasyStockDbContext(
            new DbContextOptionsBuilder<EasyStockDbContext>()
                .UseInMemoryDatabase($"order-incompleto-{Guid.NewGuid()}").Options,
            currentUser);
    }

    public void Dispose() => _db.Dispose();

    [Theory]
    [InlineData("{\"id\":\"o-1\"}", "clientSnapshot")]
    [InlineData("{\"clientSnapshot\":{\"name\":\"Ana\"},\"items\":[]}", "id")]
    [InlineData("{\"id\":\"o-1\",\"clientSnapshot\":{\"name\":\"Ana\"}}", "items")]
    [InlineData("{\"id\":\"o-1\",\"clientSnapshot\":null,\"items\":null}", "clientSnapshot")]
    [InlineData("{\"id\":\"o-1\",\"clientSnapshot\":{\"name\":\"Ana\"},\"items\":null}", "items")]
    [InlineData("{\"id\":5,\"clientSnapshot\":{\"name\":\"Ana\"},\"items\":[]}", "campo id deve ser texto")]
    [InlineData("{\"id\":\"o-1\",\"clientSnapshot\":{\"name\":\"Ana\"},\"items\":\"x\"}", "campo items deve ser lista")]
    [InlineData("{\"id\":\"o-1\",\"clientSnapshot\":\"Ana\",\"items\":[]}", "campo clientSnapshot deve ser objeto")]
    public async Task OrderUpsertIncompleto_EhRecusadoComMotivoLegivel(string payloadJson, string campo)
    {
        var payload = JsonDocument.Parse(payloadJson).RootElement.Clone();
        var mutacao = new MutationDto(Guid.NewGuid().ToString(), "dev-a", "order.upsert", payload, 1);

        var act = () => Dispatcher().ApplyMutationAsync(mutacao, "dev-a", null, _empresaId, Guid.NewGuid());

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage($"*order.upsert*{campo}*");
    }

    private SyncMutationDispatcher Dispatcher()
        => new(
            _db,
            new MobileStockReconciler(_db, new MobileSystemUserResolver(_db), NullLogger<MobileStockReconciler>.Instance),
            new LoteMobileEstadoReconciler(_db, NullLogger<LoteMobileEstadoReconciler>.Instance),
            new MobileSaleSyncService(_db, NullLogger<MobileSaleSyncService>.Instance),
            new OperacaoEventBroker(NullLogger<OperacaoEventBroker>.Instance),
            Substitute.For<IProdutoRepository>(),
            DispatcherDeTeste.EstornoDeCaixa(_db),
            NullLogger<SyncMutationDispatcher>.Instance);
}
