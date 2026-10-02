using EasyStock.Application.Services.Auth;
using EasyStock.Application.Tests.Services.Auth;
using EasyStock.Application.UseCases.AceitarConvite;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.TestHelpers;

namespace EasyStock.Application.Tests.UseCases;

/// <summary>
/// N9: o aceite é o POST. O uso único é de verdade (UPDATE condicional), o canal do token é verificado no mesmo passo,
/// os outros convites morrem e toda recusa responde igual.
/// </summary>
public class AceitarConviteUseCaseTests
{
    private const string NovaSenha = "Nova@Senha123";
    private const string Ip = "198.51.100.20";
    private const string MensagemDeRecusa = "Convite inválido ou expirado.";
    private readonly CenarioDeAcesso _c = new();

    private async Task<(string Email, string? WhatsApp)> EmitirAsync(Usuario usuario, bool comWhatsApp = true)
    {
        await _c.Convites().EmitirAsync(usuario, _c.EmpresaPadraoId, comWhatsApp, Ip, "Teste/1.0");
        var payload = _c.Eventos[^1].Payload;
        var link = payload.GetProperty("link_convite").GetString()!;
        var email = Uri.UnescapeDataString(link[(link.IndexOf("token=", StringComparison.Ordinal) + "token=".Length)..]);
        var whats = payload.TryGetProperty("token_convite_whatsapp", out var t) ? t.GetString() : null;
        return (email, whats);
    }

    private Task<AceitarConviteResult> Aceitar(string token, string senha = NovaSenha, string? ip = Ip) =>
        _c.AceitarConvite().ExecuteAsync(new AceitarConviteCommand(token, senha, ip, "Navegador/2.0"));

    [Theory]
    [InlineData("Email")]
    [InlineData("WhatsApp")]
    public async Task DefineSenhaEVerificaOCanalDoToken(string canal)
    {
        var usuario = _c.CriarConvidado(telefone: "+5511999991234");
        var (email, whats) = await EmitirAsync(usuario);

        var resultado = await Aceitar(canal == "Email" ? email : whats!);

        resultado.Success.Should().BeTrue();
        usuario.SenhaHash.Should().Be(FakePasswordHasher.MakeHash(NovaSenha));
        usuario.ConvitePendente.Should().BeFalse();
        usuario.ConviteAceitoEm.Should().Be(_c.AgoraUtc);
        if (canal == "Email")
        {
            usuario.EmailConfirmado.Should().BeTrue();
            usuario.TelefoneVerificadoEm.Should().BeNull("o token de e-mail só prova o e-mail");
            usuario.ConviteAceitoVia.Should().Be("e-mail");
        }
        else
        {
            usuario.TelefoneVerificadoEm.Should().Be(_c.AgoraUtc);
            usuario.EmailConfirmado.Should().BeFalse("o token de WhatsApp só prova o telefone");
            usuario.ConviteAceitoVia.Should().Be("+55•••1234");
        }

        _c.UnitOfWork.CommitCount.Should().Be(1);
        _c.Auditorias.Should().ContainSingle(a => a.Acao == "aceitar-convite" && a.Sucesso);
    }

    [Fact]
    public async Task AbrirNaoConsomeEOMesmoLinkAindaFuncionaNoPost()
    {
        var usuario = _c.CriarConvidado();
        var (email, _) = await EmitirAsync(usuario, comWhatsApp: false);

        // Scanner de e-mail que abre o link (GET) nunca chama a API: nada a consumir. O token segue aberto até o POST.
        _c.Tokens.Linhas.Should().OnlyContain(l => !l.Usado);

        (await Aceitar(email)).Success.Should().BeTrue();
    }

    [Fact]
    public async Task UsoUnicoUmSoPassa()
    {
        var usuario = _c.CriarConvidado();
        var (email, _) = await EmitirAsync(usuario, comWhatsApp: false);

        var primeiro = await Aceitar(email);
        var segundo = () => Aceitar(email, "Outra@Senha456");

        primeiro.Success.Should().BeTrue();
        await segundo.Should().ThrowAsync<RegraDeDominioVioladaException>().WithMessage(MensagemDeRecusa);
        usuario.SenhaHash.Should().Be(FakePasswordHasher.MakeHash(NovaSenha), "a segunda tentativa não troca a senha");
    }

    [Fact]
    public async Task PerdedorDaCorridaDoUpdateNaoTocaNaConta()
    {
        var usuario = _c.CriarConvidado();
        var (email, _) = await EmitirAsync(usuario, comWhatsApp: false);
        _c.Tokens.PerderACorridaDoConsumo = true; // dois POST simultâneos: o UPDATE condicional afeta 0 linhas

        var act = () => Aceitar(email);

        await act.Should().ThrowAsync<RegraDeDominioVioladaException>().WithMessage(MensagemDeRecusa);
        usuario.ConvitePendente.Should().BeTrue();
        usuario.EmailConfirmado.Should().BeFalse();
        _c.UnitOfWork.CommitCount.Should().Be(0);
    }

    [Fact]
    public async Task AceitarPorWhatsAppGravaOsOptInsDaVerificacao()
    {
        var usuario = _c.CriarConvidado(telefone: "+5511999991234");
        var (_, whats) = await EmitirAsync(usuario);

        await Aceitar(whats!);

        _c.ConsentimentosGravados.Should().HaveCount(2).And.OnlyContain(c =>
            c.UsuarioId == usuario.Id && c.Canal == CanalNotificacao.WhatsApp && c.OptIn
            && c.AtualizadoPor == $"convite:{usuario.Id}" && c.IpOrigem == Ip);
        _c.ConsentimentosGravados.Select(c => c.Categoria).Should()
            .BeEquivalentTo([CategoriaConteudoNotificacao.Seguranca, CategoriaConteudoNotificacao.Operacional]);
    }

    [Fact]
    public async Task AceitarPorEmailNaoGravaOptInDeWhatsApp()
    {
        var usuario = _c.CriarConvidado(telefone: "+5511999991234");
        var (email, _) = await EmitirAsync(usuario);

        await Aceitar(email);

        _c.ConsentimentosGravados.Should().BeEmpty();
    }

    [Fact]
    public async Task AceitarRevogaOsOutrosConvites()
    {
        var usuario = _c.CriarConvidado(telefone: "+5511999991234");
        var (email, whats) = await EmitirAsync(usuario);

        await Aceitar(email);

        _c.Tokens.Linhas.Should().OnlyContain(l => l.Usado);
        var act = () => Aceitar(whats!, "Outra@Senha456");
        await act.Should().ThrowAsync<RegraDeDominioVioladaException>().WithMessage(MensagemDeRecusa);
    }

    [Fact]
    public async Task TokenDeResetNaoAceitaConvite()
    {
        var usuario = _c.CriarConvidado();
        var reset = ResetToken.Criar(
            usuario.Id, SegredosDeAcesso.HashDoLink("texto-do-reset"), _c.AgoraUtc.AddMinutes(30), null, null);
        await _c.Tokens.AddAsync(reset);

        var act = () => Aceitar("texto-do-reset");

        await act.Should().ThrowAsync<RegraDeDominioVioladaException>().WithMessage(MensagemDeRecusa);
        reset.Usado.Should().BeFalse();
        usuario.ConvitePendente.Should().BeTrue();
    }

    [Fact]
    public async Task ConviteNaoServeParaRedefinirSenha()
    {
        var usuario = _c.CriarConvidado();
        var (email, _) = await EmitirAsync(usuario, comWhatsApp: false);

        var act = () => _c.ResetarSenha().ExecuteAsync(
            new EasyStock.Application.UseCases.ResetarSenha.ResetarSenhaCommand(email, NovaSenha, Ip, null));

        await act.Should().ThrowAsync<RegraDeDominioVioladaException>();
        usuario.ConvitePendente.Should().BeTrue();
    }

    [Fact]
    public async Task ConviteVencidoRecusaComMensagemGenerica()
    {
        var usuario = _c.CriarConvidado();
        var (email, _) = await EmitirAsync(usuario, comWhatsApp: false);
        _c.Relogio.Advance(TimeSpan.FromHours(72) + TimeSpan.FromSeconds(1));

        var act = () => Aceitar(email);

        await act.Should().ThrowAsync<RegraDeDominioVioladaException>().WithMessage(MensagemDeRecusa);
        usuario.ConvitePendente.Should().BeTrue();
    }

    [Fact]
    public async Task TokenDesconhecidoEUsuarioInativoRecebemAMesmaMensagem()
    {
        var inativo = _c.CriarConvidado("inativo@casadababa.com");
        var (email, _) = await EmitirAsync(inativo, comWhatsApp: false);
        inativo.Ativo = false;

        var desconhecido = () => Aceitar("token-que-nunca-existiu");
        var deInativo = () => Aceitar(email);

        await desconhecido.Should().ThrowAsync<RegraDeDominioVioladaException>().WithMessage(MensagemDeRecusa);
        await deInativo.Should().ThrowAsync<RegraDeDominioVioladaException>().WithMessage(MensagemDeRecusa);
    }

    [Fact]
    public async Task QuemJaAceitouNaoAceitaDeNovo()
    {
        var usuario = _c.CriarConvidado();
        var (email, _) = await EmitirAsync(usuario, comWhatsApp: false);
        usuario.AceitarConvite(FakePasswordHasher.MakeHash("Outra@Senha456"), ViaDoConvite.Email, _c.AgoraUtc); // aceitou por outro caminho

        var act = () => Aceitar(email);

        await act.Should().ThrowAsync<RegraDeDominioVioladaException>().WithMessage(MensagemDeRecusa);
        usuario.SenhaHash.Should().Be(FakePasswordHasher.MakeHash("Outra@Senha456"));
    }

    [Fact]
    public async Task SuperadminRecusa()
    {
        var usuario = _c.CriarConvidado();
        var (email, _) = await EmitirAsync(usuario, comWhatsApp: false);
        CenarioDeAcesso.TornarSuperAdmin(usuario);

        var act = () => Aceitar(email);

        await act.Should().ThrowAsync<RegraDeDominioVioladaException>().WithMessage(MensagemDeRecusa);
        usuario.ConvitePendente.Should().BeTrue();
        _c.Tokens.Linhas.Single().Usado.Should().BeFalse("recusar antes de consumir deixa o rastro intacto");
    }

    [Fact]
    public async Task SenhaForaDaPoliticaRecusaSemConsumirOConvite()
    {
        var usuario = _c.CriarConvidado();
        var (email, _) = await EmitirAsync(usuario, comWhatsApp: false);

        var act = () => Aceitar(email, "123");

        await act.Should().ThrowAsync<UseCaseValidationException>();
        _c.Tokens.Linhas.Single().Usado.Should().BeFalse("quem errou a senha tenta de novo com o mesmo link");
        usuario.ConvitePendente.Should().BeTrue();
    }

    [Fact]
    public async Task SeisTentativasDoMesmoIpEstouramOLimite()
    {
        for (var i = 0; i < LimitePedidosAcesso.PedidosPorJanela; i++)
        {
            var tentativa = () => Aceitar("token-errado");
            await tentativa.Should().ThrowAsync<RegraDeDominioVioladaException>();
        }

        var estourou = () => Aceitar("token-errado");

        await estourou.Should().ThrowAsync<LimitePedidosAcessoExcedidoException>();
    }

    [Fact]
    public async Task LogNuncaLevaTokenNemEmailNemTelefone()
    {
        var logger = new LoggerQueGuarda<AceitarConviteUseCase>();
        var usuario = _c.CriarConvidado(telefone: "+5511999991234");
        var (email, whats) = await EmitirAsync(usuario);

        await _c.AceitarConvite(logger).ExecuteAsync(new AceitarConviteCommand(whats!, NovaSenha, Ip, null));
        var recusa = () => _c.AceitarConvite(logger).ExecuteAsync(new AceitarConviteCommand(email, NovaSenha, Ip, null));
        await recusa.Should().ThrowAsync<RegraDeDominioVioladaException>();

        var tudo = string.Join("\n", logger.Linhas);
        tudo.Should().NotContain(email).And.NotContain(whats!).And.NotContain("ana@casadababa.com")
            .And.NotContain("999991234").And.NotContain(NovaSenha);
    }
}
