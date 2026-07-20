using System.Security.Cryptography;
using System.Text;
using EasyStock.Api.Middleware;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Middleware;

/// <summary>
/// issue 917 Fase B: cobre o modelo INSERT-first do <see cref="IdempotencyMiddleware"/> —
/// reserva/replay/409/422/CAS-takeover/reabertura — sem Postgres real (o SqlState 23505 do
/// unique_violation em si é coberto por integração, fora do CI; ver plano da issue).
/// </summary>
public class IdempotencyMiddlewareTests
{
    private const string Header = "Idempotency-Key";
    private const string DefaultPath = "/api/pedidos/abc/pagamentos";
    private const string DefaultKey = "key-1";
    private const string DefaultBody = "{\"valor\":10}";

    private static string Sha256Hex(string s) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(s)));

    private static (IdempotencyMiddleware mw, HttpContext ctx, IIdempotencyKeyRepository repo, int[] called) Setup(
        Guid? empresaId = null,
        string? idempotencyKey = DefaultKey,
        string path = DefaultPath,
        string body = DefaultBody,
        Func<HttpContext, Task>? nextBody = null,
        IdempotencyOptions? options = null)
    {
        var calledBox = new int[1];
        RequestDelegate next = async c =>
        {
            calledBox[0]++;
            if (nextBody is not null) await nextBody(c);
            else c.Response.StatusCode = StatusCodes.Status200OK;
        };

        var repo = Substitute.For<IIdempotencyKeyRepository>();
        var opts = options ?? new IdempotencyOptions().Add("/api/pedidos");
        var mw = new IdempotencyMiddleware(next, opts, NullLogger<IdempotencyMiddleware>.Instance);

        var services = new ServiceCollection();
        var user = Substitute.For<ICurrentUserAccessor>();
        user.EmpresaId.Returns(empresaId ?? Guid.NewGuid());
        services.AddSingleton(user);
        services.AddSingleton(repo);
        var ctx = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        ctx.Request.Method = HttpMethods.Post;
        ctx.Request.Path = path;
        if (idempotencyKey is not null)
            ctx.Request.Headers[Header] = idempotencyKey;
        var bytes = Encoding.UTF8.GetBytes(body);
        ctx.Request.Body = new MemoryStream(bytes);
        ctx.Request.ContentLength = bytes.Length;
        ctx.Response.Body = new MemoryStream();

        return (mw, ctx, repo, calledBox);
    }

    [Fact]
    public async Task Metodo_nao_POST_passa_direto_sem_tocar_repo()
    {
        var (mw, ctx, repo, called) = Setup();
        ctx.Request.Method = HttpMethods.Get;

        await mw.InvokeAsync(ctx);

        called[0].Should().Be(1);
        await repo.DidNotReceiveWithAnyArgs().TryBeginAsync(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Rota_fora_da_whitelist_passa_reto_mesmo_com_header()
    {
        var (mw, ctx, repo, called) = Setup(path: "/api/produtos");

        await mw.InvokeAsync(ctx);

        called[0].Should().Be(1);
        await repo.DidNotReceiveWithAnyArgs().TryBeginAsync(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Sem_header_passa_direto_degrau_1_apenas_observa()
    {
        var (mw, ctx, repo, called) = Setup(idempotencyKey: null);

        await mw.InvokeAsync(ctx);

        called[0].Should().Be(1);
        await repo.DidNotReceiveWithAnyArgs().TryBeginAsync(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Header_vazio_retorna_400_sem_tocar_repo()
    {
        var (mw, ctx, repo, called) = Setup(idempotencyKey: "   ");

        await mw.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        called[0].Should().Be(0);
        await repo.DidNotReceiveWithAnyArgs().TryBeginAsync(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Header_maior_que_120_chars_retorna_400()
    {
        var (mw, ctx, repo, called) = Setup(idempotencyKey: new string('a', 121));

        await mw.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        called[0].Should().Be(0);
    }

    [Fact]
    public async Task EmpresaId_vazio_passa_direto_sem_tocar_repo()
    {
        var (mw, ctx, repo, called) = Setup(empresaId: Guid.Empty);

        await mw.InvokeAsync(ctx);

        called[0].Should().Be(1);
        await repo.DidNotReceiveWithAnyArgs().TryBeginAsync(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reserva_bem_sucedida_roda_next_uma_vez_e_marca_concluido_com_resposta_cacheada()
    {
        var empresaId = Guid.NewGuid();
        var (mw, ctx, repo, called) = Setup(empresaId: empresaId, nextBody: async c =>
        {
            c.Response.StatusCode = StatusCodes.Status201Created;
            await c.Response.WriteAsync("{\"ok\":true}");
        });
        var entry = IdempotencyKey.CriarPendente(DefaultKey, empresaId, $"POST {DefaultPath}", null, TimeSpan.FromHours(24));
        repo.TryBeginAsync(DefaultKey, empresaId, $"POST {DefaultPath}", Arg.Any<string?>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns((entry, true));

        await mw.InvokeAsync(ctx);

        called[0].Should().Be(1);
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status201Created);
        await repo.Received(1).MarcarConcluidoAsync(entry.Id, StatusCodes.Status201Created, "{\"ok\":true}", Arg.Any<CancellationToken>());
        await repo.DidNotReceiveWithAnyArgs().MarcarFalhouAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reserva_com_handler_4xx_marca_falhou_em_vez_de_concluido()
    {
        var empresaId = Guid.NewGuid();
        var (mw, ctx, repo, called) = Setup(empresaId: empresaId,
            nextBody: c => { c.Response.StatusCode = StatusCodes.Status400BadRequest; return Task.CompletedTask; });
        var entry = IdempotencyKey.CriarPendente(DefaultKey, empresaId, $"POST {DefaultPath}", null, TimeSpan.FromHours(24));
        repo.TryBeginAsync(DefaultKey, empresaId, $"POST {DefaultPath}", Arg.Any<string?>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns((entry, true));

        await mw.InvokeAsync(ctx);

        called[0].Should().Be(1);
        await repo.Received(1).MarcarFalhouAsync(entry.Id, Arg.Any<CancellationToken>());
        await repo.DidNotReceiveWithAnyArgs().MarcarConcluidoAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handler_lanca_excecao_marca_falhou_e_relanca()
    {
        var empresaId = Guid.NewGuid();
        var (mw, ctx, repo, called) = Setup(empresaId: empresaId,
            nextBody: _ => throw new InvalidOperationException("boom"));
        var entry = IdempotencyKey.CriarPendente(DefaultKey, empresaId, $"POST {DefaultPath}", null, TimeSpan.FromHours(24));
        repo.TryBeginAsync(DefaultKey, empresaId, $"POST {DefaultPath}", Arg.Any<string?>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns((entry, true));

        var act = () => mw.InvokeAsync(ctx);

        await act.Should().ThrowAsync<InvalidOperationException>();
        called[0].Should().Be(1);
        await repo.Received(1).MarcarFalhouAsync(entry.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Colisao_concluido_com_payload_igual_faz_replay_sem_chamar_next()
    {
        var empresaId = Guid.NewGuid();
        var (mw, ctx, repo, called) = Setup(empresaId: empresaId, body: DefaultBody);
        var hash = Sha256Hex(DefaultBody);
        var entry = IdempotencyKey.CriarPendente(DefaultKey, empresaId, $"POST {DefaultPath}", hash, TimeSpan.FromHours(24));
        entry.MarcarConcluido(201, "{\"cached\":true}", DateTime.UtcNow);
        repo.TryBeginAsync(DefaultKey, empresaId, $"POST {DefaultPath}", Arg.Any<string?>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns((entry, false));

        await mw.InvokeAsync(ctx);

        called[0].Should().Be(0);
        ctx.Response.StatusCode.Should().Be(201);
        ctx.Response.Headers["X-Idempotent-Replay"].ToString().Should().Be("true");
        ctx.Response.Body.Position = 0;
        var written = await new StreamReader(ctx.Response.Body).ReadToEndAsync();
        written.Should().Be("{\"cached\":true}");
    }

    [Fact]
    public async Task Colisao_concluido_com_payload_diferente_retorna_422_sem_chamar_next()
    {
        var empresaId = Guid.NewGuid();
        var (mw, ctx, repo, called) = Setup(empresaId: empresaId, body: DefaultBody);
        var entry = IdempotencyKey.CriarPendente(DefaultKey, empresaId, $"POST {DefaultPath}", "hash-de-outra-requisicao", TimeSpan.FromHours(24));
        entry.MarcarConcluido(201, "{\"cached\":true}", DateTime.UtcNow);
        repo.TryBeginAsync(DefaultKey, empresaId, $"POST {DefaultPath}", Arg.Any<string?>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns((entry, false));

        await mw.InvokeAsync(ctx);

        called[0].Should().Be(0);
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
    }

    [Fact]
    public async Task Colisao_pendente_com_lease_ativo_retorna_409_sem_chamar_next()
    {
        var empresaId = Guid.NewGuid();
        var (mw, ctx, repo, called) = Setup(empresaId: empresaId);
        var entry = IdempotencyKey.CriarPendente(DefaultKey, empresaId, $"POST {DefaultPath}", null, TimeSpan.FromHours(24));
        // LockedAtUtc = agora (recem criado) -> lease de 60s ainda vale.
        repo.TryBeginAsync(DefaultKey, empresaId, $"POST {DefaultPath}", Arg.Any<string?>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns((entry, false));

        await mw.InvokeAsync(ctx);

        called[0].Should().Be(0);
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        await repo.DidNotReceiveWithAnyArgs().TryTakeoverAsync(Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Colisao_pendente_com_lease_expirado_faz_takeover_e_roda_next()
    {
        var empresaId = Guid.NewGuid();
        var (mw, ctx, repo, called) = Setup(empresaId: empresaId);
        var entry = IdempotencyKey.CriarPendente(DefaultKey, empresaId, $"POST {DefaultPath}", null, TimeSpan.FromHours(24));
        entry.LockedAtUtc = DateTime.UtcNow.AddSeconds(-120); // lease de 60s expirado
        repo.TryBeginAsync(DefaultKey, empresaId, $"POST {DefaultPath}", Arg.Any<string?>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns((entry, false));
        repo.TryTakeoverAsync(entry.Id, entry.LockedAtUtc, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(true);

        await mw.InvokeAsync(ctx);

        called[0].Should().Be(1);
        await repo.Received(1).TryTakeoverAsync(entry.Id, entry.LockedAtUtc, Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Colisao_pendente_com_lease_expirado_mas_takeover_perdido_retorna_409()
    {
        var empresaId = Guid.NewGuid();
        var (mw, ctx, repo, called) = Setup(empresaId: empresaId);
        var entry = IdempotencyKey.CriarPendente(DefaultKey, empresaId, $"POST {DefaultPath}", null, TimeSpan.FromHours(24));
        entry.LockedAtUtc = DateTime.UtcNow.AddSeconds(-120);
        repo.TryBeginAsync(DefaultKey, empresaId, $"POST {DefaultPath}", Arg.Any<string?>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns((entry, false));
        // Outro concorrente ja tomou o lease entre o SELECT e este CAS.
        repo.TryTakeoverAsync(entry.Id, entry.LockedAtUtc, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(false);

        await mw.InvokeAsync(ctx);

        called[0].Should().Be(0);
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status409Conflict);
    }

    [Fact]
    public async Task Colisao_falhou_reabre_via_CAS_e_roda_next()
    {
        var empresaId = Guid.NewGuid();
        var (mw, ctx, repo, called) = Setup(empresaId: empresaId);
        var entry = IdempotencyKey.CriarPendente(DefaultKey, empresaId, $"POST {DefaultPath}", "hash-antigo", TimeSpan.FromHours(24));
        entry.MarcarFalhou(DateTime.UtcNow);
        repo.TryBeginAsync(DefaultKey, empresaId, $"POST {DefaultPath}", Arg.Any<string?>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns((entry, false));
        repo.TryReabrirAsync(entry.Id, Arg.Any<string?>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(true);

        await mw.InvokeAsync(ctx);

        called[0].Should().Be(1);
        await repo.Received(1).TryReabrirAsync(entry.Id, Arg.Any<string?>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Colisao_falhou_mas_reabertura_perdida_retorna_409()
    {
        var empresaId = Guid.NewGuid();
        var (mw, ctx, repo, called) = Setup(empresaId: empresaId);
        var entry = IdempotencyKey.CriarPendente(DefaultKey, empresaId, $"POST {DefaultPath}", "hash-antigo", TimeSpan.FromHours(24));
        entry.MarcarFalhou(DateTime.UtcNow);
        repo.TryBeginAsync(DefaultKey, empresaId, $"POST {DefaultPath}", Arg.Any<string?>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns((entry, false));
        repo.TryReabrirAsync(entry.Id, Arg.Any<string?>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(false);

        await mw.InvokeAsync(ctx);

        called[0].Should().Be(0);
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status409Conflict);
    }
}
