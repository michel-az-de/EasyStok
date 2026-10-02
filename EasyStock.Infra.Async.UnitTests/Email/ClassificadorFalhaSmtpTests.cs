using System.Net.Sockets;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Infra.Async.Email;
using FluentAssertions;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;

namespace EasyStock.Infra.Async.UnitTests.Email;

/// <summary>
/// N3 (#1351): a classificação sai do protocolo. 5xx do servidor é permanente, 4xx, rede, protocolo e teto estourado
/// são transitórios, e autenticação recusada é erro de configuração que nomeia as chaves e nunca a senha.
/// </summary>
public class ClassificadorFalhaSmtpTests
{
    private static SmtpCommandException Comando(int codigo, SmtpErrorCode erro = SmtpErrorCode.UnexpectedStatusCode, string mensagem = "resposta do servidor") =>
        new(erro, (SmtpStatusCode)codigo, mensagem);

    [Theory]
    [InlineData(550)]
    [InlineData(551)]
    [InlineData(552)]
    [InlineData(553)]
    [InlineData(554)]
    public void Codigos550A554SaoPermanentes(int codigo)
    {
        var falha = ClassificadorFalhaSmtp.Classificar(Comando(codigo, SmtpErrorCode.RecipientNotAccepted), tetoEstourado: false);

        falha.Desfecho.Should().Be(DesfechoEnvio.FalhaPermanente);
        falha.CodigoSmtp.Should().Be(codigo);
        falha.ErroDeConfiguracao.Should().BeFalse();
    }

    [Theory]
    [InlineData(421)]
    [InlineData(450)]
    [InlineData(451)]
    [InlineData(452)]
    public void Codigos421A452SaoTransitorios(int codigo)
    {
        var falha = ClassificadorFalhaSmtp.Classificar(Comando(codigo), tetoEstourado: false);

        falha.Desfecho.Should().Be(DesfechoEnvio.FalhaTransitoria);
        falha.CodigoSmtp.Should().Be(codigo);
        falha.ErroDeConfiguracao.Should().BeFalse();
    }

    [Theory]
    [InlineData(500)]
    [InlineData(501)]
    [InlineData(503)]
    [InlineData(555)]
    public void QualquerOutro5xxTambemEPermanente(int codigo)
    {
        ClassificadorFalhaSmtp.Classificar(Comando(codigo), tetoEstourado: false)
            .Desfecho.Should().Be(DesfechoEnvio.FalhaPermanente);
    }

    [Fact]
    public void AutenticacaoRecusadaEPermanenteDeConfiguracao()
    {
        const string senha = "SEGREDO-123";
        Exception[] recusas =
        [
            Comando(535, SmtpErrorCode.UnexpectedStatusCode, $"5.7.8 credenciais invalidas senha={senha}"),
            Comando(530, SmtpErrorCode.SenderNotAccepted, $"5.7.0 autenticacao necessaria senha={senha}"),
            new AuthenticationException($"535: 5.7.8 credenciais invalidas senha={senha}"),
        ];

        foreach (var recusa in recusas)
        {
            var falha = ClassificadorFalhaSmtp.Classificar(recusa, tetoEstourado: false);

            falha.Desfecho.Should().Be(DesfechoEnvio.FalhaPermanente);
            falha.ErroDeConfiguracao.Should().BeTrue();
            falha.CodigoSmtp.Should().BeOneOf(530, 535);
            falha.Detalhe.Should().Contain("Smtp:Username").And.Contain("Smtp:Password");
            falha.Detalhe.Should().NotContain(senha, "a senha nunca vai para o detalhe nem para o log");
        }
    }

    [Fact]
    public void AutenticacaoDaCaixaDeSegurancaNomeiaAsChavesDela()
    {
        var falha = ClassificadorFalhaSmtp.Classificar(Comando(535), tetoEstourado: false, chaveBase: "Smtp:Seguranca");

        falha.Detalhe.Should().Contain("Smtp:Seguranca:Username").And.Contain("Smtp:Seguranca:Password");
    }

    [Fact]
    public void RedeETimeoutSaoTransitorios()
    {
        var transitorios = new (Exception Erro, bool Teto)[]
        {
            (new SocketException((int)SocketError.ConnectionRefused), false),
            (new IOException("conexao encerrada pelo servidor"), false),
            (new TimeoutException("sem resposta"), false),
            (new OperationCanceledException("teto"), true),
            (new SmtpProtocolException("resposta invalida"), false),
            (new SslHandshakeException("certificado recusado"), false),
            (new ServiceNotConnectedException("sem conexao"), false),
            (new InvalidOperationException("qualquer outro erro inesperado"), false),
        };

        foreach (var (erro, teto) in transitorios)
        {
            var falha = ClassificadorFalhaSmtp.Classificar(erro, teto);

            falha.Desfecho.Should().Be(DesfechoEnvio.FalhaTransitoria, "{0} é transitório", erro.GetType().Name);
            falha.ErroDeConfiguracao.Should().BeFalse();
        }
    }

    [Fact]
    public void TetoEstouradoDizQueFoiOTeto()
    {
        var falha = ClassificadorFalhaSmtp.Classificar(new OperationCanceledException(), tetoEstourado: true);

        falha.Detalhe.Should().Contain("tempo limite");
    }

    [Fact]
    public void ServidorSemStartTlsOuAuthETransitorioMasApontaAConfiguracao()
    {
        var falha = ClassificadorFalhaSmtp.Classificar(
            new NotSupportedException("The SMTP server does not support the STARTTLS extension."), tetoEstourado: false);

        falha.Desfecho.Should().Be(DesfechoEnvio.FalhaTransitoria);
        falha.ErroDeConfiguracao.Should().BeTrue();
        falha.Detalhe.Should().Contain("Smtp:Modo").And.Contain("Smtp:Port");
    }

    [Fact]
    public void EnderecoInvalidoAntesDeConectarEPermanente()
    {
        var falha = ClassificadorFalhaSmtp.Classificar(new FormatException("endereco invalido"), tetoEstourado: false);

        falha.Desfecho.Should().Be(DesfechoEnvio.FalhaPermanente);
        falha.ErroDeConfiguracao.Should().BeFalse();
    }

    [Fact]
    public void DetalheNaoVazaEnderecoDeEmailNemEstouraOTamanho()
    {
        var longa = "5.1.1 <maria.souza@example.com>: Recipient address rejected: " + new string('x', 400);

        var falha = ClassificadorFalhaSmtp.Classificar(Comando(550, SmtpErrorCode.RecipientNotAccepted, longa), tetoEstourado: false);

        falha.Detalhe.Should().NotContain("maria.souza").And.NotContain("example.com");
        falha.Detalhe.Should().Contain("550");
        falha.Detalhe.Length.Should().BeLessThanOrEqualTo(200);
    }
}
