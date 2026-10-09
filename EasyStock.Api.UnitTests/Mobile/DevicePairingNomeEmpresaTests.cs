using EasyStock.Api.Mobile.Controllers;
using EasyStock.Application.Ports.Output;
using EasyStock.Domain.Entities;
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
/// Regressao #1474: depois de parear com a Casa da Baba o topo do PWA seguia
/// "Minha empresa", porque a resposta do pareamento nao trazia nome nenhum.
/// </summary>
public sealed class DevicePairingNomeEmpresaTests : IDisposable
{
    private readonly EasyStockDbContext _db;
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly Guid _lojaId = Guid.NewGuid();

    public DevicePairingNomeEmpresaTests()
    {
        var anonimo = Substitute.For<ICurrentUserAccessor>();
        anonimo.IsAuthenticated.Returns(false);
        _db = new EasyStockDbContext(
            new DbContextOptionsBuilder<EasyStockDbContext>()
                .UseInMemoryDatabase($"pairing-nome-{Guid.NewGuid()}")
                .Options,
            anonimo);
    }

    public void Dispose() => _db.Dispose();

    private DevicePairingController Controller() =>
        new(_db, Substitute.For<ICurrentUserAccessor>(), new ConfigurationBuilder().Build(),
            NullLogger<DevicePairingController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

    private async Task SeedAsync(string? nomeFantasia)
    {
        _db.Set<Empresa>().Add(new Empresa { Id = _empresaId, Nome = "Casa da Baba Ltda", NomeFantasia = nomeFantasia });
        _db.Set<Loja>().Add(new Loja { Id = _lojaId, EmpresaId = _empresaId, Nome = "Casa da Baba | Centro", Ativa = true });
        _db.Set<MobileDevice>().Add(new MobileDevice
        {
            Id = "pending-1",
            ApiKeyHash = "placeholder",
            EmpresaId = _empresaId,
            LojaId = _lojaId,
            PairingCode = "123456",
            PairingExpiresAt = DateTime.UtcNow.AddMinutes(10),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task Pair_devolve_nome_fantasia_e_nome_da_loja()
    {
        await SeedAsync(nomeFantasia: "Casa da Baba");

        var result = await Controller().Pair(new PairRequest("123456", "dev-abc", null), CancellationToken.None);

        var body = result.Result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeOfType<PairResponse>().Subject;
        body.EmpresaNome.Should().Be("Casa da Baba");
        body.LojaNome.Should().Be("Casa da Baba | Centro");
    }

    [Fact]
    public async Task Pair_sem_nome_fantasia_usa_o_nome_da_empresa()
    {
        await SeedAsync(nomeFantasia: null);

        var result = await Controller().Pair(new PairRequest("123456", "dev-abc", null), CancellationToken.None);

        var body = result.Result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeOfType<PairResponse>().Subject;
        body.EmpresaNome.Should().Be("Casa da Baba Ltda");
    }

    /// <summary>
    /// #1474: o aparelho que já foi pareado (ou teve o pareamento revogado) volta com o mesmo
    /// deviceId. Antes o pareamento tentava inserir de novo o mesmo id e respondia 409.
    /// </summary>
    [Fact]
    public async Task Pair_do_mesmo_aparelho_de_novo_reaproveita_o_registro()
    {
        await SeedAsync(nomeFantasia: "Casa da Baba");
        _db.Set<MobileDevice>().Add(new MobileDevice
        {
            Id = "dev-abc", ApiKeyHash = "chave-antiga", EmpresaId = _empresaId, LojaId = Guid.NewGuid(),
            Revoked = true, RevokedAt = DateTime.UtcNow.AddDays(-1),
            PairedAt = DateTime.UtcNow.AddDays(-10), CreatedAt = DateTime.UtcNow.AddDays(-10), UpdatedAt = DateTime.UtcNow.AddDays(-1),
        });
        await _db.SaveChangesAsync();

        var result = await Controller().Pair(new PairRequest("123456", "dev-abc", null), CancellationToken.None);

        var body = result.Result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeOfType<PairResponse>().Subject;
        body.LojaId.Should().Be(_lojaId);
        var registros = await _db.Set<MobileDevice>().AsNoTracking().ToListAsync();
        registros.Should().ContainSingle();
        var aparelho = registros.Single();
        aparelho.Id.Should().Be("dev-abc");
        aparelho.Revoked.Should().BeFalse();
        aparelho.LojaId.Should().Be(_lojaId);
        aparelho.ApiKeyHash.Should().NotBe("chave-antiga");
        aparelho.PairingCode.Should().BeNull();
    }
}
