using EasyStock.Application.Services.Auth;
using EasyStock.Application.Tests.Services.Auth;
using EasyStock.Application.UseCases.EsqueciSenha;
using EasyStock.Application.UseCases.ResetarSenha;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Domain.ValueObjects;
using EasyStock.TestHelpers;

namespace EasyStock.Application.Tests.UseCases;

/// <summary>N8: o pedido de redefinição só mexe em banco e enfileira UM evento no motor; o e-mail e o WhatsApp saem do Worker.</summary>
public class EsqueciSenhaUseCaseTests
{
    private readonly CenarioDeAcesso _c = new();

    private Task<EsqueciSenhaResult> Pedir(string email, string? ip = "203.0.113.7") =>
        _c.EsqueciSenha().ExecuteAsync(new EsqueciSenhaCommand(email, ip, "TesteAgent/1.0"));

    [Fact]
    public async Task EnfileiraNoMotorENaoEnviaPeloUseCase()
    {
        var usuario = _c.CriarUsuario();

        var resultado = await Pedir(usuario.Email);

        resultado.Success.Should().BeTrue();
        _c.Eventos.Should().ContainSingle().Which.Tipo.Should().Be(TipoEventoNotificacao.ResetSenha);
        _c.UnitOfWork.CommitCount.Should().Be(1, "evento e token na mesma transação (ADR-0030)");
        typeof(EsqueciSenhaUseCase).GetConstructors().Single().GetParameters()
            .Should().NotContain(p => p.ParameterType.Name == "IEmailService", "o e-mail sai do motor, não do request");
    }

    [Fact]
    public async Task ContaExistenteEInexistenteTemMesmaRespostaESemRede()
    {
        var usuario = _c.CriarUsuario();
        _c.Usuarios.GetByEmailAsync("ninguem@casadababa.com").Returns((Usuario?)null);

        var existente = await Pedir(usuario.Email);
        var inexistente = await Pedir("ninguem@casadababa.com");
        var formatoInvalido = await Pedir("isto-nao-e-email");

        existente.Should().Be(inexistente).And.Be(formatoInvalido);
        _c.Eventos.Should().HaveCount(1, "só a conta que existe gera evento");
    }

    [Fact]
    public async Task InvalidaOsSegredosAnteriores()
    {
        var usuario = _c.CriarUsuario();
        var (primeiro, _) = await _c.PedirAsync(usuario);
        _c.Relogio.Advance(TimeSpan.FromMinutes(2));
        var (segundo, _) = await _c.PedirAsync(usuario);

        var antigo = await _c.Tokens.GetByTokenAsync(primeiro);
        var novo = await _c.Tokens.GetByTokenAsync(segundo);

        antigo!.Usado.Should().BeTrue("o pedido novo mata o link do anterior");
        novo!.Usado.Should().BeFalse();
        var act = () => _c.ResetarSenha().ExecuteAsync(new ResetarSenhaCommand(primeiro, "Nova@Senha123"));
        await act.Should().ThrowAsync<RegraDeDominioVioladaException>();
    }

    [Fact]
    public async Task LinkTemTrintaMinutosETrintaEDoisBytes()
    {
        var usuario = _c.CriarUsuario();

        var (token, _) = await _c.PedirAsync(usuario);

        var linha = await _c.Tokens.GetByTokenAsync(token);
        linha!.Finalidade.Should().Be(FinalidadeResetToken.Reset);
        linha.ExpiraEm.Should().Be(_c.AgoraUtc.AddMinutes(30));
        Convert.FromBase64String(token.Replace('-', '+').Replace('_', '/') + "=").Should().HaveCount(32);
        linha.TokenHash.Should().NotContain(token, "só o hash vai ao banco");
        _c.Eventos[0].Payload.GetProperty("expira_em_minutos").GetInt32().Should().Be(30);
    }

    [Fact]
    public async Task BaseDoLinkVemDaConfiguracaoNuncaDoCorpo()
    {
        var usuario = _c.CriarUsuario();

        await _c.PedirAsync(usuario);

        _c.Eventos[0].Payload.GetProperty("link_redefinicao").GetString()
            .Should().StartWith("https://app.easystok.com.br/auth/redefinir-senha?token=");
        typeof(EsqueciSenhaCommand).GetProperties().Select(p => p.Name).Should().NotContain("BaseUrl");
    }

    [Fact]
    public async Task SemLinkConfiguradoUsaAOrigemConfiavelEOPadraoDoWeb()
    {
        _c.Configuracao.Remove("Auth:LinkRedefinirSenha");
        _c.Configuracao["Auth:TrustedLinkOrigins:0"] = "https://web.easystok.com.br/";
        var usuario = _c.CriarUsuario();

        await _c.PedirAsync(usuario);

        _c.Eventos[0].Payload.GetProperty("link_redefinicao").GetString()
            .Should().StartWith("https://web.easystok.com.br/auth/redefinir-senha?token=");
    }

    [Fact]
    public async Task SemBaseDeLinkNaoEnfileiraENaoInvalidaNada()
    {
        _c.Configuracao.Remove("Auth:LinkRedefinirSenha");
        var usuario = _c.CriarUsuario();

        var resultado = await Pedir(usuario.Email);

        resultado.Success.Should().BeTrue("a resposta é a mesma, a falha é de configuração");
        _c.Eventos.Should().BeEmpty();
        _c.Tokens.Linhas.Should().BeEmpty();
    }

    [Fact]
    public async Task TerceiroPedidoDaHoraPassaEOQuartoNao()
    {
        var usuario = _c.CriarUsuario();
        for (var i = 0; i < 3; i++)
        {
            await Pedir(usuario.Email);
            _c.Relogio.Advance(TimeSpan.FromMinutes(2));
        }
        _c.Eventos.Should().HaveCount(3);

        await Pedir(usuario.Email);

        _c.Eventos.Should().HaveCount(3, "o 4º pedido da hora responde 202 sem enviar");
        _c.Auditorias.Last().Acao.Should().Be("forgot-password-limitado");
    }

    [Fact]
    public async Task SeisPorDiaMesmoComAHoraLivre()
    {
        var usuario = _c.CriarUsuario();
        for (var i = 0; i < 6; i++)
        {
            await Pedir(usuario.Email);
            _c.Relogio.Advance(TimeSpan.FromHours(4));
        }
        _c.Eventos.Should().HaveCount(6);
        _c.Relogio.Advance(TimeSpan.FromHours(-3));

        await Pedir(usuario.Email);

        _c.Eventos.Should().HaveCount(6, "o 7º pedido em 24 h é recusado mesmo com a hora livre");
    }

    [Fact]
    public async Task SessentaSegundosEntrePedidos()
    {
        var usuario = _c.CriarUsuario();
        await Pedir(usuario.Email);

        _c.Relogio.Advance(TimeSpan.FromSeconds(59));
        await Pedir(usuario.Email);
        _c.Eventos.Should().HaveCount(1);

        _c.Relogio.Advance(TimeSpan.FromSeconds(2));
        await Pedir(usuario.Email);
        _c.Eventos.Should().HaveCount(2);
    }

    [Fact]
    public async Task LimiteDeContaResponde202SemEnviar()
    {
        var usuario = _c.CriarUsuario();
        await Pedir(usuario.Email);

        var resultado = await Pedir(usuario.Email);

        resultado.Success.Should().BeTrue();
        _c.Eventos.Should().HaveCount(1);
        _c.Tokens.Linhas.Where(l => l.Finalidade == FinalidadeResetToken.Reset).Should().HaveCount(1);
    }

    [Fact]
    public async Task SextoPedidoDoMesmoIpEmQuinzeMinutosLancaOLimite()
    {
        var usuario = _c.CriarUsuario();
        for (var i = 0; i < 5; i++) await Pedir("ninguem@casadababa.com");

        var act = () => Pedir(usuario.Email);

        (await act.Should().ThrowAsync<LimitePedidosAcessoExcedidoException>()).Which.RetryAfterSeconds.Should().BeGreaterThan(0);
        _c.Eventos.Should().BeEmpty();
    }

    [Fact]
    public async Task GravaIpEAgenteNaAuditoria()
    {
        var usuario = _c.CriarUsuario();

        var (token, _) = await _c.PedirAsync(usuario, ip: "198.51.100.9");

        var linha = await _c.Tokens.GetByTokenAsync(token);
        linha!.IpCriacao.Should().Be("198.51.100.9");
        linha.UserAgent.Should().Be("TesteAgent/1.0");
        var auditoria = _c.Auditorias.Should().ContainSingle(a => a.Acao == "forgot-password").Subject;
        auditoria.Ip.Should().Be("198.51.100.9");
        auditoria.UserAgent.Should().Be("TesteAgent/1.0");
    }

    [Fact]
    public async Task NaoLogaEmailNemTelefone()
    {
        var logger = new LoggerQueGuarda<EsqueciSenhaUseCase>();
        var usuario = _c.CriarUsuarioElegivelAoCodigo("maria.souza@empresa.com");
        _c.Usuarios.GetByEmailAsync("naoexiste@empresa.com").Returns((Usuario?)null);
        var useCase = _c.EsqueciSenha(logger);

        await useCase.ExecuteAsync(new EsqueciSenhaCommand(usuario.Email, "203.0.113.7"));
        await useCase.ExecuteAsync(new EsqueciSenhaCommand("naoexiste@empresa.com", "203.0.113.7"));
        await useCase.ExecuteAsync(new EsqueciSenhaCommand("isto-nao-e-email", "203.0.113.7"));

        var tudo = string.Join('\n', logger.Linhas);
        tudo.Should().NotContain("maria.souza").And.NotContain("naoexiste").And.NotContain("11999998888")
            .And.NotContain("isto-nao-e-email");
        var codigo = _c.Eventos[0].Payload.GetProperty("codigo").GetString()!;
        tudo.Should().NotContain(codigo);
        logger.Linhas.Should().Contain(l => l.Contains(usuario.Id.ToString()), "o rastro é o UsuarioId");
    }

    [Fact]
    public async Task EnfileiraUmEventoResetSenhaComLinkECodigo()
    {
        var usuario = _c.CriarUsuarioElegivelAoCodigo();

        await Pedir(usuario.Email);

        var evento = _c.Eventos.Should().ContainSingle().Subject;
        evento.Payload.GetProperty("usuarioId").GetGuid().Should().Be(usuario.Id);
        evento.Payload.GetProperty("nome").GetString().Should().Be("Ana");
        evento.Payload.GetProperty("link_redefinicao").GetString().Should().StartWith("https://app.easystok.com.br");
        evento.Payload.GetProperty("codigo").GetString().Should().MatchRegex("^[0-9]{6}$");
        evento.Payload.GetProperty("codigo_expira_em_minutos").GetInt32().Should().Be(10);
        evento.Payload.TryGetProperty("canais", out _).Should().BeFalse("sem restrição: o modo todos entrega nos dois canais");
        evento.Payload.GetProperty("chaveIdempotencia").GetString().Should().NotBeNullOrWhiteSpace();
        _c.Tokens.Linhas.Should().HaveCount(2).And.Contain(l => l.Finalidade == FinalidadeResetToken.ResetCodigo);
    }

    [Fact]
    public async Task EventoNasceNaEmpresaDoUsuarioQuandoTemUmaSo()
    {
        var usuario = _c.CriarUsuario();

        await Pedir(usuario.Email);

        _c.Eventos[0].EmpresaId.Should().Be(usuario.Empresas.Single().EmpresaId);
        _c.Tenant.Received(1).SetCurrentTenant(usuario.Empresas.Single().EmpresaId);
    }

    [Fact]
    public async Task EventoNasceNaEmpresaPadraoQuandoTemDuasOuNenhuma()
    {
        var duas = _c.CriarUsuario("duas@casadababa.com");
        duas.Empresas.Add(new UsuarioEmpresa { UsuarioId = duas.Id, EmpresaId = Guid.NewGuid(), Ativo = true });
        var nenhuma = _c.CriarUsuario("nenhuma@casadababa.com", comEmpresa: false);

        await Pedir(duas.Email);
        await Pedir(nenhuma.Email);

        _c.Eventos.Should().HaveCount(2).And.OnlyContain(e => e.EmpresaId == _c.EmpresaPadraoId);
        _c.Tenant.Received(2).SetCurrentTenant(_c.EmpresaPadraoId);
    }

    [Fact]
    public async Task ContaElegivelGeraCodigoDeSeisDigitos()
    {
        var usuario = _c.CriarUsuarioElegivelAoCodigo();

        var (_, codigo) = await _c.PedirAsync(usuario);

        codigo.Should().MatchRegex("^[0-9]{6}$");
        var linha = _c.Tokens.Linhas.Single(l => l.Finalidade == FinalidadeResetToken.ResetCodigo);
        linha.ExpiraEm.Should().Be(_c.AgoraUtc.AddMinutes(10));
        linha.Canal.Should().Be("WhatsApp");
        linha.TokenHash.Should().Be(SegredosDeAcesso.HashDoCodigo(linha.Id, codigo!));
        linha.TokenHash.Should().NotContain(codigo!);
    }

    [Fact]
    public async Task SuperadminSoRecebeOLinkPorEmail()
    {
        var usuario = _c.CriarUsuarioElegivelAoCodigo();
        CenarioDeAcesso.TornarSuperAdmin(usuario);

        await Pedir(usuario.Email);

        var payload = _c.Eventos.Should().ContainSingle().Subject.Payload;
        payload.TryGetProperty("codigo", out _).Should().BeFalse();
        payload.GetProperty("canais").EnumerateArray().Select(c => c.GetString()).Should().Equal("Email");
        _c.Tokens.Linhas.Should().ContainSingle(l => l.Finalidade == FinalidadeResetToken.Reset);
    }

    [Fact]
    public async Task SemTelefoneVerificadoSoRecebeOLink()
    {
        var usuario = _c.CriarUsuario();
        usuario.DefinirTelefone(TelefoneE164.From("+5511999998888"));
        _c.DarOptInDeWhatsApp(usuario);

        await Pedir(usuario.Email);

        var payload = _c.Eventos.Single().Payload;
        payload.TryGetProperty("codigo", out _).Should().BeFalse();
        payload.GetProperty("canais").EnumerateArray().Select(c => c.GetString()).Should().Equal("Email");
    }

    [Fact]
    public async Task SemOptInSoRecebeOLink()
    {
        var usuario = _c.CriarUsuario();
        usuario.DefinirTelefone(TelefoneE164.From("+5511999998888"));
        usuario.MarcarTelefoneVerificado(_c.AgoraUtc.AddDays(-1));

        await Pedir(usuario.Email);

        _c.Eventos.Single().Payload.GetProperty("canais").GetArrayLength().Should().Be(1);
        _c.Tokens.Linhas.Should().NotContain(l => l.Finalidade == FinalidadeResetToken.ResetCodigo);
    }

    [Fact]
    public async Task SemPlataformaLigadaSoRecebeOLink()
    {
        _c.Configuracao.Remove("Notifications:WhatsApp:Plataforma:PhoneNumberId");
        var usuario = _c.CriarUsuarioElegivelAoCodigo();

        await Pedir(usuario.Email);

        _c.Eventos.Single().Payload.TryGetProperty("codigo", out _).Should().BeFalse();
    }

    [Fact]
    public async Task UsuarioInativoNaoGeraNada()
    {
        var usuario = _c.CriarUsuario();
        usuario.Ativo = false;

        var resultado = await Pedir(usuario.Email);

        resultado.Success.Should().BeTrue();
        _c.Eventos.Should().BeEmpty();
        _c.Tokens.Linhas.Should().BeEmpty();
    }
    // ── N9: quem ainda não aceitou o convite nunca recebe token de reset ────────────────────────────────

    [Fact]
    public async Task PendenteReemiteOConviteENuncaGeraReset()
    {
        var pendente = _c.CriarConvidado();

        var resposta = await Pedir(pendente.Email);

        resposta.Success.Should().BeTrue("a resposta é a mesma de qualquer conta");
        var evento = _c.Eventos.Should().ContainSingle().Subject;
        evento.Tipo.Should().Be(TipoEventoNotificacao.ConviteAcesso);
        evento.Payload.GetProperty("usuarioId").GetGuid().Should().Be(pendente.Id);
        _c.Tokens.Linhas.Should().NotBeEmpty().And.OnlyContain(l => l.Finalidade == FinalidadeResetToken.Convite);
        _c.UnitOfWork.CommitCount.Should().Be(1);
    }

    [Fact]
    public async Task PendenteRevogaOConviteAnteriorAoReemitir()
    {
        var pendente = _c.CriarConvidado();
        await _c.Convites().EmitirAsync(pendente, _c.EmpresaPadraoId, false, null, null);
        var anterior = _c.Tokens.Linhas.Single();
        _c.Relogio.Advance(TimeSpan.FromMinutes(2));

        await Pedir(pendente.Email);

        anterior.Usado.Should().BeTrue();
        _c.Tokens.Linhas.Count(l => !l.Usado).Should().Be(1);
    }

    [Fact]
    public async Task PendenteNaoPassaDeTresConvitesPorHoraEContinuaRespondendoIgual()
    {
        var pendente = _c.CriarConvidado();
        for (var i = 0; i < 3; i++)
        {
            await Pedir(pendente.Email, ip: $"203.0.113.{10 + i}");
            _c.Relogio.Advance(TimeSpan.FromMinutes(2));
        }

        var quarto = await Pedir(pendente.Email, ip: "203.0.113.99");

        quarto.Success.Should().BeTrue();
        _c.Eventos.Should().HaveCount(3, "o quarto pedido na hora não emite nada");
    }

    [Fact]
    public async Task AposAceitarOEsqueciSenhaPassaAValer()
    {
        var usuario = _c.CriarConvidado();
        usuario.AceitarConvite(FakePasswordHasher.MakeHash("Senha@12345"), ViaDoConvite.Email, _c.AgoraUtc);

        await Pedir(usuario.Email);

        _c.Eventos.Should().ContainSingle().Which.Tipo.Should().Be(TipoEventoNotificacao.ResetSenha);
        _c.Tokens.Linhas.Should().Contain(l => l.Finalidade == FinalidadeResetToken.Reset);
    }
}
