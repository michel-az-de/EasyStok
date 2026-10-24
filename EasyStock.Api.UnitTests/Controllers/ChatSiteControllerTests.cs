using System.Diagnostics;
using System.Reflection;
using System.Text;
using EasyStock.Api.Configuration;
using EasyStock.Api.Controllers.Storefront;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Atendimento.ChatSite;
using EasyStock.Application.UseCases.FeatureFlags;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Api.UnitTests.Controllers;

/// <summary>
/// Chat do site (S36) na borda HTTP: a resposta da loja chega pelo stream em menos de 2 s, sessão
/// inválida é 403, chat desligado é 404, e cada endpoint tem o seu rate limit.
/// </summary>
public class ChatSiteControllerTests
{
    private const string Slug = "casa-da-baba";
    private readonly StorefrontEntity _loja;
    private readonly IStorefrontRepository _lojas = Substitute.For<IStorefrontRepository>();
    private readonly ITenantFeatureFlagRepository _flags = Substitute.For<ITenantFeatureFlagRepository>();
    private readonly ISessaoChatSiteRepository _sessoes = Substitute.For<ISessaoChatSiteRepository>();
    private readonly IConversaRepository _conversas = Substitute.For<IConversaRepository>();
    private readonly ChatSiteController _controller;
    private readonly CapturaStream _corpo = new();

    public ChatSiteControllerTests()
    {
        _loja = StorefrontEntity.Criar(empresaId: Guid.NewGuid(), slug: Slug, tituloPublico: "Casa da Babá", pedidoMinimoEntrega: 0m);
        _loja.Ativar();
        _lojas.GetBySlugAsync(Slug, Arg.Any<CancellationToken>()).Returns(_loja);
        _flags.ListarAtivasAsync(_loja.EmpresaId, Arg.Any<CancellationToken>())
            .Returns([FeatureCatalogo.ModuloAtendimento, FeatureCatalogo.CanalChatSite]);

        var acesso = new AcessoChatSite(_lojas, _flags, Substitute.For<ITenantContextAccessor>(), _sessoes);
        var uow = Substitute.For<IUnitOfWork>();
        _controller = new ChatSiteController(
            acesso,
            new AbrirSessaoChatSiteUseCase(acesso, _sessoes, uow),
            new EnviarMensagemVisitanteUseCase(acesso, _conversas, Substitute.For<IOperacaoEventPublisher>(), uow,
                NullLogger<EnviarMensagemVisitanteUseCase>.Instance, new EasyStock.Application.Services.Atendimento.ConversaChatSiteService(_conversas, uow)),
            new ListarMensagensChatSiteUseCase(acesso, _sessoes, _conversas));
        var http = new DefaultHttpContext();
        http.Response.Body = _corpo;
        _controller.ControllerContext = new ControllerContext { HttpContext = http };
    }

    private (SessaoChatSite Sessao, string Token) Sessao()
    {
        var token = AcessoChatSite.NovoToken();
        var sessao = SessaoChatSite.Abrir(_loja.EmpresaId, _loja.Id, AcessoChatSite.HashDoToken(token), DateTime.UtcNow);
        _sessoes.ObterPorTokenHashAsync(_loja.EmpresaId, sessao.TokenHash, Arg.Any<CancellationToken>()).Returns(sessao);
        _sessoes.ObterSnapshotPorTokenHashAsync(_loja.EmpresaId, sessao.TokenHash, Arg.Any<CancellationToken>()).Returns(sessao);
        return (sessao, token);
    }

    [Fact]
    public async Task RespostaDaLojaChegaPeloStreamEmMenosDeDoisSegundos()
    {
        var (sessao, token) = Sessao();
        var conversaId = Guid.NewGuid();
        sessao.VincularConversa(conversaId);
        Mensagem? resposta = null;
        _conversas.ListarMensagensDepoisAsync(_loja.EmpresaId, conversaId, Arg.Any<DateTime?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ => resposta is null ? [] : new[] { resposta });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var stream = _controller.Stream(Slug, token, null, cts.Token);
        await Task.Delay(300);

        var relogio = Stopwatch.StartNew();
        resposta = Mensagem.Saida(_loja.EmpresaId, conversaId, AutorMensagem.Dona, DateTime.UtcNow, TipoConteudoMensagem.Texto, "Entregamos sim!", "chatsite:1");
        var chegou = await _corpo.EsperarAsync("Entregamos sim!", TimeSpan.FromSeconds(5));
        relogio.Stop();
        await cts.CancelAsync();
        await stream;

        chegou.Should().BeTrue();
        relogio.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));
        _controller.Response.ContentType.Should().Be("text/event-stream");
        _corpo.Texto.Should().Contain("event: mensagem");
    }

    /// <summary>
    /// #2004 atendimento-04: a resposta da loja leva o instante do início do envio, mas só é gravada depois de esperar a
    /// trava da conversa. Se a mensagem do visitante é gravada antes, o cursor do stream já passou do instante dela e a
    /// resposta nunca chegava ao widget. O stream relê com folga e não repete o que já mandou.
    /// </summary>
    [Fact]
    public async Task RespostaDaLojaGravadaDepoisComInstanteAnteriorAoCursorAindaChegaPeloStream()
    {
        var (sessao, token) = Sessao();
        var conversaId = Guid.NewGuid();
        sessao.VincularConversa(conversaId);
        var t0 = DateTime.UtcNow;
        var visitante = Mensagem.Entrada(_loja.EmpresaId, conversaId, t0.AddMilliseconds(20), TipoConteudoMensagem.Texto,
            "Oi, ainda ai?", "chatsite:visitante-1");
        var loja = Mensagem.Saida(_loja.EmpresaId, conversaId, AutorMensagem.Dona, t0, TipoConteudoMensagem.Texto,
            "Entregamos sim!", "chatsite:loja-1");

        // O que "está no banco" agora: a consulta devolve só o que é posterior ao cursor, como a do repositório.
        var gravadas = new List<Mensagem> { visitante };
        _conversas.ListarMensagensDepoisAsync(_loja.EmpresaId, conversaId, Arg.Any<DateTime?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var cursor = ci.ArgAt<DateTime?>(2);
                lock (gravadas) return (IReadOnlyList<Mensagem>)gravadas.Where(m => cursor is null || m.EnviadaEm > cursor)
                    .OrderBy(m => m.EnviadaEm).ToList();
            });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var stream = _controller.Stream(Slug, token, null, cts.Token);
        (await _corpo.EsperarAsync("Oi, ainda ai?", TimeSpan.FromSeconds(5))).Should().BeTrue("o stream lê a do visitante e move o cursor");

        lock (gravadas) gravadas.Add(loja); // a resposta da atendente só agora termina de gravar, com o instante antigo
        var chegou = await _corpo.EsperarAsync("Entregamos sim!", TimeSpan.FromSeconds(6));
        await Task.Delay(1500); // mais uma rodada: a mensagem do visitante, relida pela folga, não pode sair de novo
        await cts.CancelAsync();
        await stream;

        chegou.Should().BeTrue();
        System.Text.RegularExpressions.Regex.Matches(_corpo.Texto, "Oi, ainda ai").Should().HaveCount(1);
        System.Text.RegularExpressions.Regex.Matches(_corpo.Texto, "Entregamos sim!").Should().HaveCount(1);
    }

    [Fact]
    public async Task StreamComTokenInvalido403()
    {
        var result = await _controller.Stream(Slug, "inventado", null, default);

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        _corpo.Texto.Should().BeEmpty();
    }

    [Fact]
    public async Task MensagemComTokenInvalido403()
    {
        var result = await _controller.EnviarMensagem(Slug, "inventado", new MensagemVisitanteBody("oi"), default);

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    private IdentificarVisitanteChatSiteUseCase Identificacao() => new(
        new AcessoChatSite(_lojas, _flags, Substitute.For<ITenantContextAccessor>(), _sessoes), _conversas, Substitute.For<IUnitOfWork>(),
        Substitute.For<IOperacaoEventPublisher>());

    [Fact]
    public async Task IdentificacaoValida200SemAceite400TokenInvalido403()
    {
        // #1430: o formulário antes do chat.
        var (sessao, token) = Sessao();
        var valido = new IdentificacaoVisitanteBody("Maria Souza", "(11) 98765-4321", null, true);

        (await _controller.Identificar(Slug, token, valido, Identificacao(), default)).Should().BeOfType<OkObjectResult>();
        sessao.ContatoInformado!.Nome.Should().Be("Maria Souza");

        (await _controller.Identificar(Slug, token, valido with { AceitePrivacidade = false }, Identificacao(), default))
            .Should().BeOfType<BadRequestObjectResult>();
        (await _controller.Identificar(Slug, "inventado", valido, Identificacao(), default))
            .Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    // #1643-4: corpo vazio com token inventado diz sessão inválida (403), não 400; o binder deixa o corpo vazio passar.
    [Fact]
    public async Task IdentificacaoSemCorpoComTokenInventado403()
    {
        Sessao();

        (await _controller.Identificar(Slug, "inventado", null, Identificacao(), default))
            .Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        typeof(ChatSiteController).GetMethod(nameof(ChatSiteController.Identificar))!.GetParameters()
            .Single(p => p.Name == "body").GetCustomAttribute<FromBodyAttribute>()!.EmptyBodyBehavior
            .Should().Be(Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow);
    }

    [Fact]
    public async Task ChatDesligado404()
    {
        _flags.ListarAtivasAsync(_loja.EmpresaId, Arg.Any<CancellationToken>()).Returns([FeatureCatalogo.ModuloAtendimento]);

        (await _controller.AbrirSessao(Slug, default)).Should().BeOfType<NotFoundObjectResult>();
    }

    [Theory]
    [InlineData(nameof(ChatSiteController.AbrirSessao), ChatSiteRateLimit.AbrirSessao)]
    [InlineData(nameof(ChatSiteController.EnviarMensagem), ChatSiteRateLimit.Mensagem)]
    [InlineData(nameof(ChatSiteController.ListarMensagens), ChatSiteRateLimit.Leitura)]
    [InlineData(nameof(ChatSiteController.Stream), ChatSiteRateLimit.Stream)]
    [InlineData(nameof(ChatSiteController.Identificar), ChatSiteRateLimit.Identificacao)]
    public void CadaEndpointTemRateLimit(string acao, string politica) =>
        typeof(ChatSiteController).GetMethod(acao)!.GetCustomAttribute<EnableRateLimitingAttribute>()!
            .PolicyName.Should().Be(politica);

    // Por sessão para o visitante não gastar a cota do vizinho no mesmo NAT, sem o token cru na chave. #1622-1: só o token
    // no formato gerado vira sessão; o inventado (ou ausente) cai no IP, e o teto por IP (LimitePorIpPublicoTests) segura o resto.
    [Fact]
    public void LimiteDeMensagemEhPorSessaoENaoGuardaOToken()
    {
        var token = AcessoChatSite.NovoToken();
        var comToken = new DefaultHttpContext();
        comToken.Request.Headers[ChatSiteRateLimit.HeaderToken] = token;
        var outroToken = new DefaultHttpContext();
        outroToken.Request.Headers[ChatSiteRateLimit.HeaderToken] = AcessoChatSite.NovoToken();
        var inventado = new DefaultHttpContext();
        inventado.Request.Headers[ChatSiteRateLimit.HeaderToken] = "tok-1";

        var chave = ChatSiteRateLimit.ChaveMensagem(comToken);

        chave.Should().StartWith("sessao:").And.NotContain(token);
        chave.Should().NotBe(ChatSiteRateLimit.ChaveMensagem(outroToken));
        ChatSiteRateLimit.ChaveMensagem(inventado).Should().StartWith("ip:").And.NotContain("tok-1");
        ChatSiteRateLimit.ChaveMensagem(new DefaultHttpContext()).Should().StartWith("ip:");
    }

    /// <summary>Corpo da resposta que o teste lê enquanto o stream escreve.</summary>
    private sealed class CapturaStream : Stream
    {
        private readonly StringBuilder _texto = new();
        private readonly object _lock = new();

        public string Texto { get { lock (_lock) return _texto.ToString(); } }

        public async Task<bool> EsperarAsync(string trecho, TimeSpan limite)
        {
            var fim = DateTime.UtcNow + limite;
            while (DateTime.UtcNow < fim)
            {
                if (Texto.Contains(trecho, StringComparison.Ordinal)) return true;
                await Task.Delay(20);
            }
            return false;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            lock (_lock) _texto.Append(Encoding.UTF8.GetString(buffer, offset, count));
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        {
            Write(buffer, offset, count);
            return Task.CompletedTask;
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
        {
            lock (_lock) _texto.Append(Encoding.UTF8.GetString(buffer.Span));
            return ValueTask.CompletedTask;
        }

        public override void Flush() { }
        public override Task FlushAsync(CancellationToken ct) => Task.CompletedTask;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
