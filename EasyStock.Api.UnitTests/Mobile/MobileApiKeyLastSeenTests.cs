using System.Net;
using EasyStock.Api.Mobile.Security;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Mobile;

/// <summary>
/// #1566: o PWA pareado faz polling a cada 30 s e cada request autenticado por chave de aparelho
/// disparava um UPDATE de <c>last_seen_at</c>. O filtro só grava quando o valor lido está nulo,
/// velho (1 min ou mais) ou quando o IP mudou. A gravação abre um escopo de DI próprio, então
/// contar <c>CreateScope</c> é contar gravações.
/// </summary>
public sealed class MobileApiKeyLastSeenTests : IDisposable
{
    private const string ApiKey = "chave-de-teste";
    private const string Ip = "10.0.0.5";

    private readonly EasyStockDbContext _db;
    private readonly EscoposContados _escopos = new();

    public MobileApiKeyLastSeenTests()
    {
        var anonimo = Substitute.For<ICurrentUserAccessor>();
        anonimo.IsAuthenticated.Returns(false);
        _db = new EasyStockDbContext(
            new DbContextOptionsBuilder<EasyStockDbContext>()
                .UseInMemoryDatabase($"last-seen-{Guid.NewGuid()}")
                .Options,
            anonimo);
    }

    public void Dispose() => _db.Dispose();

    [Theory]
    [InlineData(null)]
    [InlineData(60)]
    [InlineData(300)]
    public async Task Grava_quando_nunca_foi_visto_ou_o_ultimo_contato_passou_de_um_minuto(int? segundosAtras)
    {
        await SemearAsync(segundosAtras, Ip);

        await AutenticarAsync(Ip);

        (await _escopos.AguardarGravacaoAsync(TimeSpan.FromSeconds(5))).Should().BeTrue();
    }

    [Fact]
    public async Task Nao_grava_de_novo_no_polling_de_30_segundos()
    {
        await SemearAsync(segundosAtras: 30, Ip);

        var device = await AutenticarAsync(Ip);

        device.Should().NotBeNull("a autenticação não depende da gravação");
        (await _escopos.AguardarGravacaoAsync(TimeSpan.FromMilliseconds(300))).Should().BeFalse();
        _escopos.Criados.Should().Be(0);
    }

    [Fact]
    public async Task Grava_dentro_do_minuto_quando_o_ip_mudou()
    {
        await SemearAsync(segundosAtras: 30, Ip);

        await AutenticarAsync("10.0.0.99");

        (await _escopos.AguardarGravacaoAsync(TimeSpan.FromSeconds(5))).Should().BeTrue();
    }

    private async Task SemearAsync(int? segundosAtras, string ip)
    {
        _db.Set<MobileDevice>().Add(new MobileDevice
        {
            Id = "dev-1",
            ApiKeyHash = TokenHashHelper.ComputeSha256Hash(ApiKey),
            EmpresaId = Guid.NewGuid(),
            LojaId = Guid.NewGuid(),
            LastSeenAt = segundosAtras is { } s ? DateTime.UtcNow.AddSeconds(-s) : null,
            LastSeenIp = ip,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
    }

    private async Task<MobileDevice?> AutenticarAsync(string ipDoRequest)
    {
        var servicos = new Servicos
        {
            [typeof(EasyStockDbContext)] = _db,
            [typeof(IServiceScopeFactory)] = _escopos,
            [typeof(IConfiguration)] = new ConfigurationBuilder().Build(),
            [typeof(ILogger<>).MakeGenericType(TipoDoFiltro)] =
                Activator.CreateInstance(typeof(NullLogger<>).MakeGenericType(TipoDoFiltro))!,
        };
        var filtro = (IAsyncActionFilter)new MobileApiKeyAttribute().CreateInstance(servicos);

        var http = new DefaultHttpContext();
        http.Request.Headers[MobileAuth.HeaderName] = ApiKey;
        http.Connection.RemoteIpAddress = IPAddress.Parse(ipDoRequest);
        var acao = new ActionContext(http, new RouteData(), new ActionDescriptor());
        var contexto = new ActionExecutingContext(acao, [], new Dictionary<string, object?>(), controller: new object());

        await filtro.OnActionExecutionAsync(contexto,
            () => Task.FromResult(new ActionExecutedContext(acao, [], controller: new object())));

        contexto.Result.Should().BeNull("a chave é válida");
        return http.GetMobileDevice();
    }

    /// <summary>O filtro é internal: o tipo sai do atributo público que o instancia.</summary>
    private static Type TipoDoFiltro => new MobileApiKeyAttribute().ImplementationType;

    private sealed class Servicos : Dictionary<Type, object>, IServiceProvider
    {
        public object? GetService(Type serviceType) => TryGetValue(serviceType, out var servico) ? servico : null;
    }

    /// <summary>Conta os escopos abertos pela gravação em segundo plano. O escopo não resolve nada: a gravação falha e o filtro só loga.</summary>
    private sealed class EscoposContados : IServiceScopeFactory
    {
        private readonly TaskCompletionSource _gravou = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _criados;

        public int Criados => Volatile.Read(ref _criados);

        public IServiceScope CreateScope()
        {
            Interlocked.Increment(ref _criados);
            _gravou.TrySetResult();
            return Substitute.For<IServiceScope>();
        }

        public async Task<bool> AguardarGravacaoAsync(TimeSpan espera) =>
            await Task.WhenAny(_gravou.Task, Task.Delay(espera)) == _gravou.Task;
    }
}
