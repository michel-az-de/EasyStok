using System.Net;
using System.Net.Mail;
using System.Net.Sockets;
using System.Text;
using FluentAssertions;

namespace EasyStock.Infra.Async.UnitTests.Email;

/// <summary>
/// N2: o <see cref="SmtpEmailService"/> faz uma tentativa só. Quem repete é o outbox (backoff de 1, 5 e 30 min,
/// uma linha de log por tentativa). A retentativa aninhada (laço do serviço, Polly do canal e outbox) fazia até 36
/// tentativas SMTP por mensagem e tratava o 550 como transitório. O servidor SMTP é falso, em loopback, e conta os
/// <c>RCPT TO</c> que chegam.
/// </summary>
public class SmtpEmailServiceTests
{
    private static SmtpEmailService Servico(ServidorSmtpFalso servidor) =>
        new("127.0.0.1", servidor.Porta, "usuario", "senha", "avisos@exemplo.test", "EasyStok", enableSsl: false);

    [Fact]
    public async Task Envio_aceito_nao_lanca_e_chega_uma_vez_ao_servidor()
    {
        // Controle do harness: sem isto o teste do 550 poderia passar por uma falha de conexão qualquer.
        await using var servidor = new ServidorSmtpFalso(respostaRcpt: "250 2.1.5 Ok");
        using var servico = Servico(servidor);

        await servico.SendAsync("para@exemplo.test", "Assunto", "Corpo");

        servidor.ChamadasRcptTo.Should().Be(1);
    }

    [Fact]
    public async Task Smtp_550_e_tentado_uma_vez()
    {
        await using var servidor = new ServidorSmtpFalso(respostaRcpt: "550 5.1.1 Mailbox unavailable");
        using var servico = Servico(servidor);

        var act = () => servico.SendAsync("para@exemplo.test", "Assunto", "Corpo");

        var ex = await act.Should().ThrowAsync<SmtpException>();
        ex.Which.StatusCode.Should().Be(SmtpStatusCode.MailboxUnavailable);
        servidor.ChamadasRcptTo.Should().Be(1, "550 nunca passa e o serviço não repete: o laço antigo fazia 3 RCPT TO");
    }

    [Fact]
    public async Task Smtp_421_tambem_e_tentado_uma_vez_pelo_servico()
    {
        // O 421 é transitório, mas quem decide repetir é o outbox, com backoff de minutos, não um laço de 2 s.
        await using var servidor = new ServidorSmtpFalso(respostaRcpt: "421 4.3.2 Service not available");
        using var servico = Servico(servidor);

        var act = () => servico.SendAsync("para@exemplo.test", "Assunto", "Corpo");

        var ex = await act.Should().ThrowAsync<SmtpException>();
        ((int)ex.Which.StatusCode).Should().BeGreaterThanOrEqualTo(400).And.BeLessThan(500);
        servidor.ChamadasRcptTo.Should().Be(1);
    }

    /// <summary>SMTP mínimo em loopback: aceita o remetente, responde <paramref name="respostaRcpt"/> ao destinatário.</summary>
    private sealed class ServidorSmtpFalso : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _cts = new();
        private readonly string _respostaRcpt;
        private readonly Task _aceitando;
        private int _chamadasRcptTo;

        public ServidorSmtpFalso(string respostaRcpt)
        {
            _respostaRcpt = respostaRcpt;
            _listener.Start();
            _aceitando = Task.Run(AceitarAsync);
        }

        public int Porta => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public int ChamadasRcptTo => Volatile.Read(ref _chamadasRcptTo);

        private async Task AceitarAsync()
        {
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    var cliente = await _listener.AcceptTcpClientAsync(_cts.Token);
                    _ = Task.Run(() => AtenderAsync(cliente));
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                // Servidor encerrado pelo teste.
            }
        }

        private async Task AtenderAsync(TcpClient cliente)
        {
            using (cliente)
            {
                try
                {
                    var rede = cliente.GetStream();
                    using var leitor = new StreamReader(rede, Encoding.ASCII);
                    await using var escritor = new StreamWriter(rede, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };

                    await escritor.WriteLineAsync("220 localhost ESMTP falso");
                    var emDados = false;
                    while (await leitor.ReadLineAsync(_cts.Token) is { } linha)
                    {
                        if (emDados)
                        {
                            if (linha == ".")
                            {
                                emDados = false;
                                await escritor.WriteLineAsync("250 2.0.0 Ok");
                            }

                            continue;
                        }

                        var comando = linha.ToUpperInvariant();
                        if (comando.StartsWith("EHLO", StringComparison.Ordinal) || comando.StartsWith("HELO", StringComparison.Ordinal))
                            await escritor.WriteLineAsync("250 localhost");
                        else if (comando.StartsWith("MAIL FROM", StringComparison.Ordinal))
                            await escritor.WriteLineAsync("250 2.1.0 Ok");
                        else if (comando.StartsWith("RCPT TO", StringComparison.Ordinal))
                        {
                            Interlocked.Increment(ref _chamadasRcptTo);
                            await escritor.WriteLineAsync(_respostaRcpt);
                        }
                        else if (comando.StartsWith("DATA", StringComparison.Ordinal))
                        {
                            emDados = true;
                            await escritor.WriteLineAsync("354 Pode enviar");
                        }
                        else if (comando.StartsWith("RSET", StringComparison.Ordinal))
                            await escritor.WriteLineAsync("250 2.0.0 Ok");
                        else if (comando.StartsWith("QUIT", StringComparison.Ordinal))
                        {
                            await escritor.WriteLineAsync("221 2.0.0 Tchau");
                            break;
                        }
                        else
                            await escritor.WriteLineAsync("500 5.5.2 Comando desconhecido");
                    }
                }
                catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
                {
                    // O cliente fechou a conexão ou o teste terminou.
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _cts.CancelAsync();
            _listener.Stop();
            await _aceitando;
            _cts.Dispose();
        }
    }
}
