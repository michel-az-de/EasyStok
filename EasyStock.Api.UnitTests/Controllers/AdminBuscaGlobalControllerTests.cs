using EasyStock.Api.Controllers;
using EasyStock.Api.Services;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Infra.Postgre.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Controllers;

/// <summary>
/// ADM-008 (#698): a busca global extraía os dígitos do termo e fazia ILIKE %dígitos% no
/// CNPJ. Um único dígito (ex: o "1" de um payload "&lt;img ...alert(1)&gt;") casava qualquer
/// CNPJ contendo "1". A guarda só aciona o match por documento com 3+ dígitos.
/// </summary>
public class AdminBuscaGlobalControllerTests : IDisposable
{
    private readonly IAdminBuscaGlobalQueries _queries = Substitute.For<IAdminBuscaGlobalQueries>();
    private readonly EasyStockDbContext _db;
    private readonly AdminBuscaGlobalController _controller;

    public AdminBuscaGlobalControllerTests()
    {
        _db = new EasyStockDbContext(new DbContextOptionsBuilder<EasyStockDbContext>()
            .UseInMemoryDatabase($"admin-busca-tests-{Guid.NewGuid()}")
            .Options);

        var http = Substitute.For<IHttpContextAccessor>();
        http.HttpContext.Returns((HttpContext?)null);
        var audit = new AdminAuditService(_db, http, NullLogger<AdminAuditService>.Instance);

        _queries.BuscarAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new AdminBuscaGlobalResultado(
                Array.Empty<BuscaClienteRow>(), Array.Empty<BuscaLojaRow>(), Array.Empty<BuscaUsuarioRow>()));

        _controller = new AdminBuscaGlobalController(_queries, audit);
    }

    [Theory]
    [InlineData("<img src=x onerror=alert(1)>")] // 1 dígito -> não busca por documento
    [InlineData("ab12")]                          // 2 dígitos -> não busca por documento
    public async Task Busca_com_poucos_digitos_nao_aciona_match_por_documento(string termo)
    {
        await _controller.Buscar(termo);

        // Matchers para todos os args string (NSubstitute exige specs para args do mesmo tipo).
        await _queries.Received(1).BuscarAsync(
            Arg.Any<string>(), Arg.Is<string?>(d => d == null), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Busca_com_tres_ou_mais_digitos_aciona_match_por_documento()
    {
        await _controller.Buscar("48123456");

        await _queries.Received(1).BuscarAsync(
            Arg.Any<string>(), Arg.Is<string?>(d => d == "%48123456%"), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    public void Dispose() => _db.Dispose();
}
