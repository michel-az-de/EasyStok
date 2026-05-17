using System.Text;
using EasyStock.Api.Controllers;
using EasyStock.Api.Http;
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

    private ConsentimentosController BuildController(string? secret = "segredo-de-teste-min32-chars-1234")
    {
        var optIn = new RegistrarOptInUseCase(_repo, _uow,
            NullLogger<RegistrarOptInUseCase>.Instance);
        var optOut = new RegistrarOptOutUseCase(_repo, _uow,
            NullLogger<RegistrarOptOutUseCase>.Instance);

        var dict = new Dictionary<string, string?>();
        if (secret is not null) dict["Notifications:UnsubscribeSecret"] = secret;
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
        var secret = "segredo-de-teste-min32-chars-1234";
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
        var secret = "segredo-de-teste-min32-chars-1234";
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

    private static string ComputeHmac(string secret, string message)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var msgBytes = Encoding.UTF8.GetBytes(message);
        var hash = System.Security.Cryptography.HMACSHA256.HashData(keyBytes, msgBytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
