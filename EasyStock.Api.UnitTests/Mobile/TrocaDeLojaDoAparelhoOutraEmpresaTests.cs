using EasyStock.Api.Mobile.Controllers;
using EasyStock.Api.Mobile.Security;
using EasyStock.Application.Ports.Output;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Domain.Enums;
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
/// #2342 (R2-API-6): o aparelho troca a própria loja só com a chave dele. A loja de OUTRA empresa é recusada antes de
/// gravar. O furo que sobra na issue é a troca entre lojas da mesma empresa sem aprovação, que depende de decisão de
/// produto.
/// </summary>
public sealed class TrocaDeLojaDoAparelhoOutraEmpresaTests : IDisposable
{
    private readonly Guid _empresa = Guid.NewGuid();
    private readonly Guid _lojaDoAparelho = Guid.NewGuid();
    private readonly EasyStockDbContext _db;
    private readonly DevicePairingController _controller;

    public TrocaDeLojaDoAparelhoOutraEmpresaTests()
    {
        // SuperAdmin desliga o filtro global de tenant do DbContext. Assim o teste prova a conferência do próprio
        // controller (loja da empresa do APARELHO). Na API real ainda há o filtro com o tenant do aparelho e a RLS.
        var usuario = Substitute.For<ICurrentUserAccessor>();
        usuario.IsAuthenticated.Returns(true);
        usuario.EmpresaId.Returns(_empresa);
        usuario.Nivel.Returns(NivelAcesso.SuperAdmin);
        _db = new EasyStockDbContext(
            new DbContextOptionsBuilder<EasyStockDbContext>().UseInMemoryDatabase($"troca-loja-{Guid.NewGuid()}").Options, usuario);
        _db.Add(new Loja { Id = _lojaDoAparelho, EmpresaId = _empresa, Nome = "Centro", Ativa = true });
        _db.SaveChanges();

        var http = new DefaultHttpContext();
        http.Items[MobileAuth.HttpContextItemDevice] = new MobileDevice
        {
            Id = "aparelho-centro", ApiKeyHash = "h", EmpresaId = _empresa, LojaId = _lojaDoAparelho
        };
        _controller = new DevicePairingController(_db, usuario, new ConfigurationBuilder().Build(),
            NullLogger<DevicePairingController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = http }
        };
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task LojaDeOutraEmpresa_RecusaSemTrocar()
    {
        var alheia = new Loja { Id = Guid.NewGuid(), EmpresaId = Guid.NewGuid(), Nome = "Loja alheia", Ativa = true };
        _db.Add(alheia);
        _db.SaveChanges();

        var resposta = await _controller.SwitchLoja(new SwitchLojaRequest(alheia.Id), default);

        resposta.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task LojaDaMesmaEmpresa_Troca()
    {
        // Prova de que o teste acima não passa por acaso: na mesma empresa a troca chega ao UPDATE (que o
        // banco em memória não executa). O furo da issue é exatamente este caminho sem aprovação.
        var mercado = new Loja { Id = Guid.NewGuid(), EmpresaId = _empresa, Nome = "Mercado", Ativa = true };
        _db.Add(mercado);
        _db.SaveChanges();

        var trocar = () => _controller.SwitchLoja(new SwitchLojaRequest(mercado.Id), default);

        await trocar.Should().ThrowAsync<InvalidOperationException>("passou da conferência e tentou o SQL cru");
    }
}
