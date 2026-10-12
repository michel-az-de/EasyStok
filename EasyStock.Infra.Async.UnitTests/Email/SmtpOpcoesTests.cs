using EasyStock.Infra.Async.Email;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace EasyStock.Infra.Async.UnitTests.Email;

/// <summary>
/// N3 (#1351): a seção <c>Smtp</c> resolve uma vez, igual para a API e o Worker. A segurança do transporte sai da
/// porta (465 implícito, o resto STARTTLS obrigatório) e nunca de um "Auto" que aceita rebaixar para texto puro.
/// </summary>
public class SmtpOpcoesTests
{
    private const string Producao = "Production";

    private static SmtpOpcoes Base(Action<SmtpOpcoes>? ajustar = null)
    {
        var opcoes = new SmtpOpcoes
        {
            Host = "smtp.exemplo.com",
            Port = "587",
            Username = "avisos@easystok.online",
            Password = "senha-avisos",
            FromEmail = "avisos@easystok.online",
        };
        ajustar?.Invoke(opcoes);
        return opcoes;
    }

    [Fact]
    public void Porta465ResolveSslImplicito()
    {
        var configuracao = Base(o => o.Port = "465").Resolver(Producao);

        configuracao.Porta.Should().Be(465);
        configuracao.Modo.Should().Be(SmtpModo.SslImplicito);
    }

    [Fact]
    public void Porta587ResolveStartTls()
    {
        var configuracao = Base().Resolver(Producao);

        configuracao.Porta.Should().Be(587);
        configuracao.Modo.Should().Be(SmtpModo.StartTls);
    }

    [Theory]
    [InlineData("25")]
    [InlineData("2525")]
    [InlineData("8025")]
    public void PortaPersonalizadaSemModoExigeStartTls(string porta)
    {
        var configuracao = Base(o => o.Port = porta).Resolver(Producao);

        configuracao.Modo.Should().Be(SmtpModo.StartTls, "nenhuma porta cai em texto puro ou 'se o servidor oferecer'");
    }

    [Theory]
    [InlineData("465", "StartTls", SmtpModo.StartTls)]
    [InlineData("587", "SslImplicito", SmtpModo.SslImplicito)]
    [InlineData("587", "sslimplicito", SmtpModo.SslImplicito)]
    [InlineData("465", "Auto", SmtpModo.SslImplicito)]
    [InlineData("587", "Auto", SmtpModo.StartTls)]
    public void ModoExplicitoVenceAPorta(string porta, string modo, SmtpModo esperado)
    {
        var configuracao = Base(o => { o.Port = porta; o.Modo = modo; }).Resolver(Producao);

        configuracao.Modo.Should().Be(esperado);
    }

    [Fact]
    public void ModoNenhumEmProductionRecusaSubir()
    {
        var act = () => Base(o => o.Modo = "Nenhum").Resolver(Producao);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Smtp:Modo*Production*");
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Staging")]
    public void ModoNenhumValeForaDeProduction(string ambiente)
    {
        var configuracao = Base(o => { o.Port = "1025"; o.Modo = "Nenhum"; }).Resolver(ambiente);

        configuracao.Modo.Should().Be(SmtpModo.Nenhum);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void AmbienteDesconhecidoContaComoProduction(string? ambiente)
    {
        var act = () => Base(o => o.Modo = "Nenhum").Resolver(ambiente!);

        act.Should().Throw<InvalidOperationException>("na dúvida, o fail-safe é Production");
    }

    [Fact]
    public void EnableSslLegadoFalseEquivaleANenhumSoQuandoModoNaoExiste()
    {
        var legado = Base(o => { o.Port = "1025"; o.EnableSsl = "false"; }).Resolver("Development");
        legado.Modo.Should().Be(SmtpModo.Nenhum);

        var comModo = Base(o => { o.Port = "587"; o.EnableSsl = "false"; o.Modo = "StartTls"; }).Resolver(Producao);
        comModo.Modo.Should().Be(SmtpModo.StartTls, "o legado só vale quando Smtp:Modo não existe");
    }

    [Fact]
    public void EnableSslLegadoFalseEmProductionRecusaSubirNomeandoAsDuasChaves()
    {
        var act = () => Base(o => o.EnableSsl = "false").Resolver(Producao);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Smtp:EnableSsl*Smtp:Modo*");
    }

    [Fact]
    public void SemFromEmailNaoInventaDominio()
    {
        var opcoes = new SmtpOpcoes { Host = "smtp.exemplo.com", Port = "587" };

        opcoes.TentarResolver(Producao, out var configuracao, out var chaveFaltando).Should().BeFalse();
        configuracao.Should().BeNull();
        chaveFaltando.Should().Be("Smtp:FromEmail");

        var act = () => opcoes.Resolver(Producao);
        act.Should().Throw<InvalidOperationException>().WithMessage("*Smtp:FromEmail*");
    }

    [Fact]
    public void UsernameComArrobaServeDeRemetenteQuandoNaoHaFromEmail()
    {
        var opcoes = new SmtpOpcoes
        {
            Host = "smtp.exemplo.com",
            Port = "587",
            Username = "avisos@easystok.online",
            Password = "senha",
        };

        var configuracao = opcoes.Resolver(Producao);

        configuracao.Avisos.Email.Should().Be("avisos@easystok.online");
    }

    [Theory]
    [InlineData("avisos@@easystok.online")]
    [InlineData("@easystok.online")]
    public void UsernameMalformadoUsadoComoRemetenteNomeiaAChaveNaSubida(string username)
    {
        // Sem FromEmail o Username com arroba vira o From. Se ele nao e um endereco, o erro tem que estourar na subida
        // nomeando a chave, e nao em cada envio como "destinatario invalido".
        var opcoes = new SmtpOpcoes { Host = "smtp.exemplo.com", Port = "587", Username = username, Password = "x" };

        var act = () => opcoes.Resolver(Producao);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Smtp:Username*");
    }

    [Fact]
    public void UsernameDeSegurancaMalformadoUsadoComoRemetenteNomeiaAChaveNaSubida()
    {
        var act = () => Base(o => o.Seguranca = new SmtpRemetenteOpcoes { Username = "seguranca@@easystok.online", Password = "x" })
            .Resolver(Producao);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Smtp:Seguranca:Username*");
    }

    [Fact]
    public void UsernameSemArrobaNaoServeDeRemetente()
    {
        var opcoes = new SmtpOpcoes { Host = "smtp.exemplo.com", Port = "587", Username = "apikey", Password = "x" };

        opcoes.TentarResolver(Producao, out _, out var chaveFaltando).Should().BeFalse();
        chaveFaltando.Should().Be("Smtp:FromEmail");
    }

    [Theory]
    [InlineData(null, null, "Smtp:Host")]
    [InlineData("smtp.exemplo.com", null, "Smtp:Port")]
    public void ChaveObrigatoriaAusenteEReportadaPeloNome(string? host, string? porta, string esperada)
    {
        var opcoes = new SmtpOpcoes { Host = host, Port = porta, FromEmail = "avisos@easystok.online" };

        opcoes.TentarResolver(Producao, out var configuracao, out var chaveFaltando).Should().BeFalse();
        configuracao.Should().BeNull();
        chaveFaltando.Should().Be(esperada);
    }

    [Fact]
    public void RemetenteSegurancaUsaCredencialPropriaECaiNoAvisos()
    {
        // Com caixa própria: cada caixa autentica com a própria credencial.
        var propria = Base(o => o.Seguranca = new SmtpRemetenteOpcoes
        {
            Username = "seguranca@easystok.online",
            Password = "senha-seguranca",
            FromEmail = "seguranca@easystok.online",
            FromName = "EasyStok Segurança",
        }).Resolver(Producao);

        propria.SegurancaUsaAvisos.Should().BeFalse();
        propria.Seguranca.Email.Should().Be("seguranca@easystok.online");
        propria.Seguranca.Nome.Should().Be("EasyStok Segurança");
        propria.Seguranca.Username.Should().Be("seguranca@easystok.online");
        propria.Seguranca.Password.Should().Be("senha-seguranca");
        propria.Avisos.Email.Should().Be("avisos@easystok.online");
        propria.Avisos.Username.Should().Be("avisos@easystok.online");
        propria.Avisos.Password.Should().Be("senha-avisos");

        // Sem nada de Smtp:Seguranca:* cai no remetente de avisos, e a configuração diz isso (aviso na subida).
        var semPropria = Base().Resolver(Producao);

        semPropria.SegurancaUsaAvisos.Should().BeTrue();
        semPropria.Seguranca.Email.Should().Be(semPropria.Avisos.Email);
        semPropria.Seguranca.Username.Should().Be(semPropria.Avisos.Username);
        semPropria.Seguranca.Password.Should().Be(semPropria.Avisos.Password);
    }

    [Fact]
    public void SegurancaSoComFromEmailAutenticaComACredencialDeAvisos()
    {
        var configuracao = Base(o => o.Seguranca = new SmtpRemetenteOpcoes { FromEmail = "seguranca@easystok.online" })
            .Resolver(Producao);

        configuracao.SegurancaUsaAvisos.Should().BeFalse();
        configuracao.Seguranca.Email.Should().Be("seguranca@easystok.online");
        configuracao.Seguranca.Username.Should().Be("avisos@easystok.online");
        configuracao.Seguranca.Password.Should().Be("senha-avisos");
    }

    [Fact]
    public void ChavesLegadasContinuamValendo()
    {
        // Os nomes do .env da Onda 0 (Smtp__Host, Smtp__Port, Smtp__Username, Smtp__Password, Smtp__FromEmail,
        // mais FromName e EnableSsl do appsettings antigo) seguem válidos sem mudança.
        var configuracao = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Smtp:Host"] = "smtp.hostinger.com",
                ["Smtp:Port"] = "465",
                ["Smtp:Username"] = "avisos@easystok.online",
                ["Smtp:Password"] = "segredo",
                ["Smtp:FromEmail"] = "avisos@easystok.online",
                ["Smtp:FromName"] = "EasyStok",
                ["Smtp:EnableSsl"] = "true",
            })
            .Build()
            .GetSection("Smtp")
            .Get<SmtpOpcoes>()!
            .Resolver(Producao);

        configuracao.Host.Should().Be("smtp.hostinger.com");
        configuracao.Porta.Should().Be(465);
        configuracao.Modo.Should().Be(SmtpModo.SslImplicito);
        configuracao.Avisos.Email.Should().Be("avisos@easystok.online");
        configuracao.Avisos.Nome.Should().Be("EasyStok");
        configuracao.Avisos.Username.Should().Be("avisos@easystok.online");
        configuracao.Avisos.Password.Should().Be("segredo");
    }

    [Fact]
    public void ChavesAninhadasDeSegurancaVindasDaConfiguracaoSaoLidas()
    {
        var configuracao = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Smtp:Host"] = "smtp.hostinger.com",
                ["Smtp:Port"] = "587",
                ["Smtp:FromEmail"] = "avisos@easystok.online",
                ["Smtp:Seguranca:Username"] = "seguranca@easystok.online",
                ["Smtp:Seguranca:Password"] = "outro-segredo",
                ["Smtp:Seguranca:FromEmail"] = "seguranca@easystok.online",
            })
            .Build()
            .GetSection("Smtp")
            .Get<SmtpOpcoes>()!
            .Resolver(Producao);

        configuracao.Seguranca.Email.Should().Be("seguranca@easystok.online");
        configuracao.Seguranca.Password.Should().Be("outro-segredo");
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("70000")]
    [InlineData("58 7")]
    public void PortaInvalidaNomeiaAChave(string porta)
    {
        var act = () => Base(o => o.Port = porta).Resolver(Producao);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Smtp:Port*");
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-5")]
    public void TimeoutInvalidoNomeiaAChave(string timeout)
    {
        var act = () => Base(o => o.TimeoutSegundos = timeout).Resolver(Producao);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Smtp:TimeoutSegundos*");
    }

    [Fact]
    public void ModoInvalidoNomeiaAChave()
    {
        var act = () => Base(o => o.Modo = "Ssl").Resolver(Producao);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Smtp:Modo*Auto*SslImplicito*StartTls*Nenhum*");
    }

    [Fact]
    public void TimeoutPadraoEVinteSegundosEOConfiguradoVale()
    {
        Base().Resolver(Producao).Timeout.Should().Be(TimeSpan.FromSeconds(20));
        Base(o => o.TimeoutSegundos = "2").Resolver(Producao).Timeout.Should().Be(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void SenhaNuncaApareceNoTextoDaConfiguracao()
    {
        var configuracao = Base(o => o.Seguranca = new SmtpRemetenteOpcoes
        {
            Username = "seguranca@easystok.online",
            Password = "senha-seguranca",
            FromEmail = "seguranca@easystok.online",
        }).Resolver(Producao);

        configuracao.ToString().Should().NotContain("senha-avisos").And.NotContain("senha-seguranca");
    }
}
