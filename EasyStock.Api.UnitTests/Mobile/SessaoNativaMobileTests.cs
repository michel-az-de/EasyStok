using System.Text.Json;
using EasyStock.Api.Authentication;
using EasyStock.Api.Mobile.Controllers;
using EasyStock.Api.Mobile.Security;
using EasyStock.Api.Services.Operacao;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Infra.Postgre.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Mobile;

public sealed class SessaoNativaMobileTests : IDisposable
{
    private const string Chave = "credencial-sintetica-2184";
    private readonly EasyStockDbContext _db;
    private readonly IRefreshTokenRepository _tokens = Substitute.For<IRefreshTokenRepository>();
    private readonly IUsuarioRepository _usuarios = Substitute.For<IUsuarioRepository>();
    private readonly IConfiguration _config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Jwt:SecretKey"] = "chave-de-teste-sse-com-mais-de-32-caracteres-2184", ["Jwt:Issuer"] = "teste", ["Jwt:Audience"] = "teste"
    }).Build();
    private readonly SessaoNativa _sessao = new() { UsuarioId = Guid.NewGuid(), EmpresaId = Guid.NewGuid(), DispositivoId = "android-2184", CriadaEm = DateTime.UtcNow.AddMinutes(-1) };
    private readonly MobileDevice _device;
    private readonly ValidadorSessaoUsuario _validador;

    public SessaoNativaMobileTests()
    {
        var current = Substitute.For<ICurrentUserAccessor>();
        current.IsAuthenticated.Returns(false);
        _db = new EasyStockDbContext(new DbContextOptionsBuilder<EasyStockDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, current);
        _device = new MobileDevice { Id = _sessao.DispositivoId, EmpresaId = _sessao.EmpresaId, LojaId = Guid.NewGuid(),
            SessaoNativaId = _sessao.Id, ApiKeyHash = TokenHashHelper.ComputeSha256Hash(Chave), LastSeenAt = DateTime.UtcNow, LastSeenIp = "" };
        _db.Add(_device);
        _db.SaveChanges();
        _tokens.ObterSessaoNativaAsync(_sessao.Id).Returns(_sessao);
        _usuarios.ObterSessaoAsync(_sessao.UsuarioId).Returns(new SessaoDoUsuario(true, null));
        _validador = new(Substitute.For<ICacheService>(), _usuarios, _config, NullLogger<ValidadorSessaoUsuario>.Instance, _tokens);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task ChaveVinculadaAFamiliaEncerradaRecebe401SemEntrarNaAcao()
    {
        var servicos = new ServiceCollection().AddSingleton(_db).AddSingleton(_config).AddSingleton(_validador).AddLogging().BuildServiceProvider();
        var filtro = (IAsyncActionFilter)new MobileApiKeyAttribute().CreateInstance(servicos);
        async Task<bool> Chamar()
        {
            var http = new DefaultHttpContext();
            http.Request.Headers[MobileAuth.HeaderName] = Chave;
            var action = new ActionContext(http, new RouteData(), new ActionDescriptor());
            var context = new ActionExecutingContext(action, [], new Dictionary<string, object?>(), new object());
            var entrou = false;
            await filtro.OnActionExecutionAsync(context, () => { entrou = true; return Task.FromResult(new ActionExecutedContext(action, [], new object())); });
            if (!entrou) Assert.IsType<UnauthorizedResult>(context.Result);
            return entrou;
        }
        Assert.True(await Chamar());
        _sessao.RevogadaEm = DateTime.UtcNow;
        Assert.False(await Chamar());
    }

    [Fact]
    public async Task SseDaFamiliaAntigaNaoSeAssociaAoNovoLoginDoMesmoAparelho()
    {
        var emissor = Controller();
        emissor.HttpContext.Items[MobileAuth.HttpContextItemDevice] = _device;
        var response = Assert.IsType<OkObjectResult>(emissor.IssueSseToken());
        var token = JsonSerializer.SerializeToElement(response.Value).GetProperty("token").GetString();
        _device.SessaoNativaId = Guid.NewGuid();
        await _db.SaveChangesAsync();
        var controller = Controller();
        await controller.Stream(token, null, CancellationToken.None);
        Assert.Equal(401, controller.Response.StatusCode);
    }

    [Fact]
    public async Task SseAbertoEncerraNoHeartbeatAposSair()
    {
        var controller = Controller();
        controller.Response.Body = new MemoryStream();
        using var limite = new CancellationTokenSource(TimeSpan.FromSeconds(32));
        var stream = controller.Stream(null, Chave, limite.Token);
        Assert.True(controller.Response.Body.Length > 0, "a conexão já enviou ready antes do logout");
        _sessao.RevogadaEm = DateTime.UtcNow;
        await stream.WaitAsync(TimeSpan.FromSeconds(29));
        Assert.False(limite.IsCancellationRequested, "a revogação encerra o stream, sem esperar o cliente cancelar");
    }

    [Fact]
    public async Task SseEChaveLegadaNaUrlTambemRecusamFamiliaEncerrada()
    {
        _sessao.RevogadaEm = DateTime.UtcNow;
        var controller = Controller();
        await controller.Stream(null, Chave, CancellationToken.None);
        Assert.Equal(401, controller.Response.StatusCode);
    }

    private OperationController Controller()
    {
        var http = new DefaultHttpContext { RequestServices = new ServiceCollection().AddSingleton(_validador).BuildServiceProvider() };
        return new(_db, new OperacaoEventBroker(NullLogger<OperacaoEventBroker>.Instance), Substitute.For<ICurrentUserAccessor>(),
            _config, NullLogger<OperationController>.Instance) { ControllerContext = new ControllerContext { HttpContext = http } };
    }
}
