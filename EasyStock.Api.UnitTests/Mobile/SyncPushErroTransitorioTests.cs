using System.Text.Json;
using EasyStock.Api.Mobile.Controllers;
using EasyStock.Api.Mobile.DTOs;
using EasyStock.Api.Mobile.Security;
using EasyStock.Api.Mobile.Services;
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
using Npgsql;

namespace EasyStock.Api.UnitTests.Mobile;

// #2317 (SYNC-9): erro transitório no push não pode virar rejeição permanente.
// A mutação precisa ficar fora de accepted e de rejected, para o aparelho reenviar.
public sealed class SyncPushErroTransitorioTests : IDisposable
{
    [Fact]
    public void ConcorrenciaDoBancoEhTransitoria()
        => SyncController.EhErroTransitorio(new DbUpdateConcurrencyException("linha alterada")).Should().BeTrue();

    [Fact]
    public void TimeoutEhTransitorio()
        => SyncController.EhErroTransitorio(new TimeoutException("tempo esgotado")).Should().BeTrue();

    [Fact]
    public void QuedaDeConexaoEhTransitoria()
        => SyncController.EhErroTransitorio(new IOException("conexão encerrada")).Should().BeTrue();

    [Fact]
    public void ErroDeValidacaoEhPermanente()
        => SyncController.EhErroTransitorio(new InvalidOperationException("payload inválido")).Should().BeFalse();

    private static PostgresException Pg(string sqlState)
        => new("erro", "ERROR", "ERROR", sqlState);

    [Theory]
    [InlineData("22P02")]
    [InlineData("23505")]
    [InlineData("23503")]
    public void ErroPermanenteDoPostgresNaoEhTransitorio(string sqlState)
    {
        SyncController.EhErroTransitorio(Pg(sqlState)).Should().BeFalse();
        SyncController.EhErroTransitorio(new DbUpdateException("x", Pg(sqlState))).Should().BeFalse();
    }

    [Theory]
    [InlineData("40001")]
    [InlineData("40P01")]
    public void FalhaTransitoriaDoPostgresEhTransitoria(string sqlState)
    {
        SyncController.EhErroTransitorio(Pg(sqlState)).Should().BeTrue();
        SyncController.EhErroTransitorio(new DbUpdateException("x", Pg(sqlState))).Should().BeTrue();
        SyncController.EhErroTransitorio(new InvalidOperationException("x", Pg(sqlState))).Should().BeTrue();
    }

    // --- nível do controller (achado 2): o lote transitório não grava estado parcial ---

    private sealed class DispatcherQueFalha : SyncMutationDispatcher
    {
        private readonly EasyStockDbContext _ctx;
        public DispatcherQueFalha(EasyStockDbContext ctx) : base(ctx, null!, null!, null!, null!, null!, null!, NullLogger<SyncMutationDispatcher>.Instance) => _ctx = ctx;

        public override Task ApplyMutationAsync(MutationDto m, string deviceId, string? operatorName, Guid? empresaId, Guid? lojaId)
        {
            _ctx.Add(new Product { Id = "p-" + m.Id, Name = "X", UpdatedAt = DateTime.UtcNow, LastDeviceId = deviceId, EmpresaId = empresaId ?? Guid.Empty });
            if (m.Type == "product.falha") throw new TimeoutException("banco lento");
            return Task.CompletedTask;
        }
    }

    private readonly EasyStockDbContext _db;
    private readonly string _nome = $"sync-transitorio-{Guid.NewGuid()}";

    public SyncPushErroTransitorioTests()
    {
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.IsAuthenticated.Returns(true);
        _db = new EasyStockDbContext(
            new DbContextOptionsBuilder<EasyStockDbContext>().UseInMemoryDatabase(_nome).Options, currentUser);
    }

    public void Dispose() => _db.Dispose();

    private static MutationDto Mut(string id, string type)
        => new(id, "dev", type, JsonDocument.Parse("{}").RootElement.Clone(), 1);

    [Fact]
    public async Task PushComErroTransitorio_Devolve503ENaoGravaNadaDoLote()
    {
        var http = new DefaultHttpContext();
        http.Items[MobileAuth.HttpContextItemDevice] = new MobileDevice { Id = "dev", ApiKeyHash = "h", EmpresaId = Guid.NewGuid(), LojaId = Guid.NewGuid() };
        var controller = new SyncController(
            _db, new DispatcherQueFalha(_db), null!, null!, null!,
            Substitute.For<IProdutoRepository>(),
            new ConfigurationBuilder().Build(),
            TimeProvider.System,
            new NumeroCurtoDosLotesMobile(_db, Substitute.For<ILoteNumeroCurtoRepository>(), NullLogger<NumeroCurtoDosLotesMobile>.Instance),
            NullLogger<SyncController>.Instance)
        { ControllerContext = new ControllerContext { HttpContext = http } };

        var result = await controller.Push(new SyncPushRequest("dev", [Mut("m1", "product.ok"), Mut("m2", "product.falha")]));

        result.Result.Should().BeAssignableTo<ObjectResult>().Which.StatusCode.Should().Be(503);
        using var leitura = new EasyStockDbContext(
            new DbContextOptionsBuilder<EasyStockDbContext>().UseInMemoryDatabase(_nome).Options,
            Substitute.For<ICurrentUserAccessor>());
        (await leitura.Set<Product>().IgnoreQueryFilters().CountAsync()).Should().Be(0, "nenhuma entidade parcial pode ser gravada");
        (await leitura.MobileProcessedMutations.CountAsync()).Should().Be(0);
    }
}
