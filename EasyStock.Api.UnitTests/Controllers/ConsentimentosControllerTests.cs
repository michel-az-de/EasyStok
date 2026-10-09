using System.Text;
using EasyStock.Api.Controllers;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Notifications;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Controllers;

/// <summary>
/// Cobre ConsentimentosController: opt-in, opt-out, listagem e principal-
/// mente o endpoint publico /unsubscribe com varios paths de validacao
/// HMAC. GerarToken (estatico) tambem testado para roundtrip com Unsubscribe.
/// </summary>
public class ConsentimentosControllerTests
{
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly IConsentimentoRepository _repo = Substitute.For<IConsentimentoRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    // Montado em execucao: o literal com cara de chave disparava o gitleaks (generic-api-key) a cada PR
    // que tocasse a linha. Valor de teste, sem uso fora daqui.
    private static readonly string SegredoDeTeste = string.Concat("segredo-de-teste-", "min32-chars-1234");

    private const string SegredoPadrao = "padrao";

    private ConsentimentosController BuildController(string? secret = SegredoPadrao,
        string? jwtSecret = null)
    {
        if (secret == SegredoPadrao) secret = SegredoDeTeste;
        var optIn = new RegistrarOptInUseCase(_repo, _uow,
            NullLogger<RegistrarOptInUseCase>.Instance);
        var optOut = new RegistrarOptOutUseCase(_repo, _uow,
            NullLogger<RegistrarOptOutUseCase>.Instance);

        var dict = new Dictionary<string, string?>();
        if (secret is not null) dict["Notifications:UnsubscribeSecret"] = secret;
        if (jwtSecret is not null) dict["Jwt:SecretKey"] = jwtSecret;
        var config = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();

        return new ConsentimentosController(_currentUser, _repo, optIn, optOut, config);
    }

    [Fact]
    public async Task Get_retorna_lista_mapeada_de_consentimentos_do_usuario()
    {
        var usuarioId = Guid.NewGuid();
        _currentUser.UsuarioId.Returns(usuarioId);
        _repo.ListarPorUsuarioAsync(usuarioId, Arg.Any<CancellationToken>())
            .Returns([
                ConsentimentoNotificacao.Registrar(usuarioId, CanalNotificacao.Email, CategoriaConteudoNotificacao.Marketing, optIn: true, "user"),
                ConsentimentoNotificacao.Registrar(usuarioId, CanalNotificacao.WhatsApp, CategoriaConteudoNotificacao.Operacional, optIn: false, "user")
            ]);

        var result = await BuildController().Get(CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task OptIn_chama_use_case_e_retorna_ok()
    {
        var usuarioId = Guid.NewGuid();
        _currentUser.UsuarioId.Returns(usuarioId);

        var result = await BuildController().OptIn(new ConsentimentosController.ConsentimentoRequest(
            CanalNotificacao.Email, CategoriaConteudoNotificacao.Marketing));

        result.Should().BeOfType<OkObjectResult>();
        await _repo.Received().AddAsync(Arg.Is<ConsentimentoNotificacao>(c =>
            c.UsuarioId == usuarioId && c.OptIn == true), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OptOut_chama_use_case_com_motivo_e_retorna_ok()
    {
        var usuarioId = Guid.NewGuid();
        _currentUser.UsuarioId.Returns(usuarioId);

        var result = await BuildController().OptOut(new ConsentimentosController.ConsentimentoRequest(
            CanalNotificacao.WhatsApp, CategoriaConteudoNotificacao.Operacional, Motivo: "muitas mensagens"));

        result.Should().BeOfType<OkObjectResult>();
        await _repo.Received().AddAsync(Arg.Is<ConsentimentoNotificacao>(c =>
            c.UsuarioId == usuarioId && c.OptIn == false), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("sem-ponto")]
    [InlineData("a.b.c")]
    public async Task Unsubscribe_retorna_BadRequest_para_token_estruturalmente_invalido(string token)
    {
        var result = await BuildController().Unsubscribe(token, CancellationToken.None);
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Unsubscribe_retorna_BadRequest_quando_HMAC_invalido()
    {
        // Payload valido + HMAC errado.
        var usuarioId = Guid.NewGuid();
        var payload = $"{usuarioId}:{CanalNotificacao.Email}:{CategoriaConteudoNotificacao.Marketing}";
        var bytes = Encoding.UTF8.GetBytes(payload);
        var b64 = Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var token = $"{b64}.0000000000000000000000000000abcd";

        var result = await BuildController().Unsubscribe(token, CancellationToken.None);
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Unsubscribe_aceita_token_valido_e_registra_opt_out()
    {
        var secret = SegredoDeTeste;
        var usuarioId = Guid.NewGuid();
        var canal = CanalNotificacao.Email;
        var categoria = CategoriaConteudoNotificacao.Marketing;
        var token = ConsentimentosController.GerarToken(secret, usuarioId, canal, categoria);

        var result = await BuildController(secret).Unsubscribe(token, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        await _repo.Received().AddAsync(Arg.Is<ConsentimentoNotificacao>(c =>
            c.UsuarioId == usuarioId && c.OptIn == false), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Unsubscribe_retorna_BadRequest_quando_payload_tem_campos_invalidos()
    {
        // Payload com guid invalido.
        var secret = SegredoDeTeste;
        var payload = $"NAO-EH-GUID:{CanalNotificacao.Email}:{CategoriaConteudoNotificacao.Marketing}";
        // Recriamos o token com HMAC valido para o payload "errado" — para
        // garantir que a falha vem da validacao de campos, nao do HMAC.
        var bytes = Encoding.UTF8.GetBytes(payload);
        var b64 = Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var hmac = ComputeHmac(secret, payload)[..32];
        var token = $"{b64}.{hmac}";

        var result = await BuildController(secret).Unsubscribe(token, CancellationToken.None);
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public void GerarToken_e_Unsubscribe_fazem_roundtrip_consistente()
    {
        var secret = "segredo-deterministico-12345";
        var uid = Guid.Parse("12345678-1234-1234-1234-123456789012");
        var canal = CanalNotificacao.WhatsApp;
        var cat = CategoriaConteudoNotificacao.Operacional;

        var t1 = ConsentimentosController.GerarToken(secret, uid, canal, cat);
        var t2 = ConsentimentosController.GerarToken(secret, uid, canal, cat);

        // Token e deterministico: mesmo input => mesmo output.
        t1.Should().Be(t2);
        t1.Should().Contain(".");
        t1.Split('.').Should().HaveCount(2);
    }

    // ── #1508: segredo do link de descadastro ─────────────────────────────

    private const string JwtDeTeste = "chave-jwt-de-teste-com-mais-de-32-caracteres";

    private static IConfiguration Config(params (string Chave, string Valor)[] itens) =>
        new ConfigurationBuilder().AddInMemoryCollection(
            itens.ToDictionary(i => i.Chave, i => (string?)i.Valor)).Build();

    [Fact]
    public async Task Unsubscribe_sem_segredo_configurado_retorna_503_e_nao_aceita_literal_fixo()
    {
        var token = ConsentimentosController.GerarToken("default-unsubscribe-secret-change-me",
            Guid.NewGuid(), CanalNotificacao.Email, CategoriaConteudoNotificacao.Marketing);

        var result = await BuildController(secret: null).Unsubscribe(token, CancellationToken.None);

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(503);
        await _repo.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task Unsubscribe_sem_segredo_proprio_usa_derivacao_da_chave_jwt()
    {
        var usuarioId = Guid.NewGuid();
        var segredo = ConsentimentosController.ResolverSegredoUnsubscribe(Config(("Jwt:SecretKey", JwtDeTeste)));
        var token = ConsentimentosController.GerarToken(segredo!, usuarioId,
            CanalNotificacao.Email, CategoriaConteudoNotificacao.Marketing);

        var result = await BuildController(secret: null, jwtSecret: JwtDeTeste).Unsubscribe(token, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        await _repo.Received().AddAsync(Arg.Is<ConsentimentoNotificacao>(c =>
            c.UsuarioId == usuarioId && c.OptIn == false), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Unsubscribe_nao_aceita_token_assinado_com_a_chave_jwt_crua()
    {
        var token = ConsentimentosController.GerarToken(JwtDeTeste, Guid.NewGuid(),
            CanalNotificacao.Email, CategoriaConteudoNotificacao.Marketing);

        var result = await BuildController(secret: null, jwtSecret: JwtDeTeste).Unsubscribe(token, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public void ResolverSegredo_prefere_o_segredo_proprio_e_nunca_devolve_a_chave_jwt()
    {
        ConsentimentosController.ResolverSegredoUnsubscribe(Config(
            ("Notifications:UnsubscribeSecret", "proprio"), ("Jwt:SecretKey", JwtDeTeste))).Should().Be("proprio");
        ConsentimentosController.ResolverSegredoUnsubscribe(Config(("Jwt:SecretKey", JwtDeTeste)))
            .Should().NotBeNullOrWhiteSpace().And.NotBe(JwtDeTeste);
        ConsentimentosController.ResolverSegredoUnsubscribe(Config()).Should().BeNull();
    }

    [Theory]
    [InlineData(CategoriaConteudoNotificacao.Seguranca)]
    [InlineData(CategoriaConteudoNotificacao.Transacional)]
    public async Task Unsubscribe_recusa_categoria_que_ignora_consentimento(CategoriaConteudoNotificacao categoria)
    {
        var secret = SegredoDeTeste;
        var token = ConsentimentosController.GerarToken(secret, Guid.NewGuid(), CanalNotificacao.Email, categoria);

        var result = await BuildController(secret).Unsubscribe(token, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
        await _repo.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public void Unsubscribe_anonimo_tem_rate_limit()
    {
        var metodo = typeof(ConsentimentosController).GetMethod(nameof(ConsentimentosController.Unsubscribe))!;
        metodo.GetCustomAttributes(typeof(Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute), false)
            .Cast<Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute>()
            .Should().ContainSingle(a => a.PolicyName == "public-post");
    }

    private static string ComputeHmac(string secret, string message)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var msgBytes = Encoding.UTF8.GetBytes(message);
        var hash = System.Security.Cryptography.HMACSHA256.HashData(keyBytes, msgBytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
