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
/// Regressao #1509 (achado 7): o pull filtrava so por loja. Aparelho sem loja (ou sem device,
/// no modo legado) recebia produtos, clientes, pedidos, lotes e caixa de TODAS as empresas.
/// As entidades mobile_* ficam fora do filtro global de tenant, entao o filtro e explicito.
/// </summary>
public sealed class SyncPullTenantTests : IDisposable
{
    private readonly EasyStockDbContext _db;
    private readonly Guid _empresa = Guid.NewGuid();
    private readonly Guid _outraEmpresa = Guid.NewGuid();

    public SyncPullTenantTests()
    {
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.EmpresaId.Returns(_empresa);

        _db = new EasyStockDbContext(
            new DbContextOptionsBuilder<EasyStockDbContext>()
                .UseInMemoryDatabase($"sync-pull-tenant-{Guid.NewGuid()}")
                .Options,
            currentUser);
    }

    private SyncController Controller(MobileDevice? device)
    {
        var http = new DefaultHttpContext();
        if (device is not null) http.Items[MobileAuth.HttpContextItemDevice] = device;
        return new SyncController(
            _db, null!, null!, null!, null!,
            Substitute.For<IProdutoRepository>(),
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["MobileSync:PullReverse:Enabled"] = "false" })
                .Build(),
            TimeProvider.System,
            NullLogger<SyncController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = http }
        };
    }

    private async Task SemearAsync()
    {
        var ontem = DateTime.UtcNow.AddDays(-1);
        foreach (var (empresa, sufixo) in new[] { (_empresa, "minha"), (_outraEmpresa, "outra") })
        {
            _db.Add(new Product { Id = $"p-{sufixo}", Name = "Lasanha", UpdatedAt = ontem, LastDeviceId = "x", EmpresaId = empresa });
            _db.Add(new Client { Id = $"c-{sufixo}", Name = "Ana", UpdatedAt = ontem, LastDeviceId = "x", EmpresaId = empresa });
            _db.Add(new Order { Id = $"o-{sufixo}", ClientSnapshotName = "Ana", UpdatedAt = ontem, CreatedAt = ontem, LastDeviceId = "x", EmpresaId = empresa });
            _db.Add(new Batch { Id = $"b-{sufixo}", Code = "L1", CreatedAt = ontem, LastDeviceId = "x", EmpresaId = empresa });
            _db.Add(new CashEntry { Id = $"cash-{sufixo}", Description = "Troco", CreatedAt = ontem, LastDeviceId = "x", EmpresaId = empresa });
        }
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task Pull_AparelhoSemLoja_RecebeSoASuaEmpresa()
    {
        await SemearAsync();
        var device = new MobileDevice { Id = "este", ApiKeyHash = "h", EmpresaId = _empresa, LojaId = Guid.Empty };

        var resultado = await Controller(device).Pull(0, "este");

        var corpo = (resultado.Result as OkObjectResult)!.Value.Should().BeOfType<SyncPullResponse>().Subject;
        corpo.Mutations.Should().HaveCount(5);
        corpo.Mutations.Select(m => m.Payload.GetProperty("id").GetString())
            .Should().OnlyContain(id => id!.EndsWith("-minha"), "nada da outra empresa pode descer");
    }

    [Fact]
    public async Task Pull_SemAparelhoPareado_Recusa()
    {
        await SemearAsync();

        var resultado = await Controller(device: null).Pull(0, "anonimo");

        resultado.Result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    public void Dispose() => _db.Dispose();
}
