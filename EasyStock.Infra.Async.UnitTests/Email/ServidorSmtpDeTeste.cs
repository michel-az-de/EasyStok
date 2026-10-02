using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace EasyStock.Infra.Async.UnitTests.Email;

/// <summary>
/// Servidor SMTP minimo em loopback, so para os testes do <c>SmtpEmailService</c> (N3, #1351). Fala o
/// suficiente do protocolo para o MailKit (EHLO, STARTTLS, AUTH PLAIN, MAIL, RCPT, DATA, QUIT) e deixa o teste
/// escolher o defeito: servidor mudo, 421 na saudacao, 550 no RCPT, travado no DATA, 535 no AUTH, com ou sem
/// TLS. Conta conexoes e destinatarios para provar "uma tentativa por chamada" e "nenhuma conexao aberta".
/// </summary>
internal sealed class ServidorSmtpDeTeste : IAsyncDisposable
{
    internal enum ModoTls
    {
        /// <summary>Texto puro, sem STARTTLS.</summary>
        Nenhum,

        /// <summary>Anuncia STARTTLS no EHLO.</summary>
        StartTls,

        /// <summary>TLS ja na conexao (porta 465).</summary>
        Implicito,
    }

    internal sealed record MensagemRecebida(string Remetente, IReadOnlyList<string> Destinatarios, string Bruto, string? UsuarioAutenticado);

    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _parar = new();
    private readonly ConcurrentBag<Task> _atendimentos = [];
    private readonly ConcurrentQueue<MensagemRecebida> _mensagens = new();
    private readonly TaskCompletionSource _dataIniciado = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private X509Certificate2? _certificado;
    private Task? _aceitador;
    private int _aceitas;
    private int _abertas;
    private int _rcpts;

    /// <summary>Linha de saudacao. Comeca com 4 ou 5: o servidor recusa e fecha.</summary>
    public string Saudacao { get; init; } = "220 localhost ESMTP de teste";

    /// <summary>Aceita a conexao e nunca responde (nem a saudacao).</summary>
    public bool Mudo { get; init; }

    public string RespostaRcpt { get; init; } = "250 2.1.5 OK";

    /// <summary>Responde 354 ao DATA e nunca confirma a mensagem.</summary>
    public bool TravarAposData { get; init; }

    public ModoTls Tls { get; init; } = ModoTls.Nenhum;

    /// <summary>Anuncia AUTH PLAIN (so depois do TLS, quando o modo tem TLS).</summary>
    public bool AnunciaAuth { get; init; }

    public string RespostaAuth { get; init; } = "235 2.7.0 Autenticado";

    /// <summary>Recusa o MAIL FROM com 530 enquanto o cliente nao autenticar.</summary>
    public bool ExigeAuth { get; init; }

    public int Porta => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public int ConexoesAceitas => Volatile.Read(ref _aceitas);

    public int ConexoesAbertas => Volatile.Read(ref _abertas);

    public int RcptRecebidos => Volatile.Read(ref _rcpts);

    public IReadOnlyCollection<MensagemRecebida> Mensagens => _mensagens;

    /// <summary>Completa quando o servidor responde 354 ao primeiro DATA (o envio ja esta no meio).</summary>
    public Task DataIniciado => _dataIniciado.Task;

    public ServidorSmtpDeTeste Iniciar()
    {
        if (Tls != ModoTls.Nenhum)
            _certificado = CertificadoAutoassinado.Criar();

        _listener.Start();
        _aceitador = Task.Run(AceitarAsync);
        return this;
    }

    private async Task AceitarAsync()
    {
        try
        {
            while (!_parar.IsCancellationRequested)
            {
                var cliente = await _listener.AcceptTcpClientAsync(_parar.Token);
                _atendimentos.Add(Task.Run(() => AtenderAsync(cliente, _parar.Token)));
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (SocketException)
        {
        }
    }

    private async Task AtenderAsync(TcpClient cliente, CancellationToken ct)
    {
        Interlocked.Increment(ref _aceitas);
        Interlocked.Increment(ref _abertas);
        try
        {
            using (cliente)
            {
                Stream fluxo = cliente.GetStream();
                if (Tls == ModoTls.Implicito)
                    fluxo = await ElevarParaTlsAsync(fluxo, ct);

                if (Mudo)
                {
                    await DescartarAteFecharAsync(fluxo, ct);
                    return;
                }

                await ConversarAsync(fluxo, ct);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException
                                       or SocketException or AuthenticationException)
        {
            // Cliente que some no meio, ou o proprio teste encerrando o servidor: nada a fazer.
        }
        finally
        {
            Interlocked.Decrement(ref _abertas);
        }
    }

    private async Task ConversarAsync(Stream fluxo, CancellationToken ct)
    {
        var canal = new Canal(fluxo);
        await canal.EscreverAsync(Saudacao, ct);
        if (Saudacao.StartsWith('4') || Saudacao.StartsWith('5'))
            return;

        var tlsAtivo = Tls == ModoTls.Implicito;
        string remetente = string.Empty;
        string? usuario = null;
        var destinatarios = new List<string>();

        while (true)
        {
            var linha = await canal.LerLinhaAsync(ct);
            if (linha is null)
                return;

            var comando = linha.ToUpperInvariant();
            if (comando.StartsWith("EHLO") || comando.StartsWith("HELO"))
            {
                var anuncios = new List<string> { "localhost", "8BITMIME", "SMTPUTF8" };
                if (Tls == ModoTls.StartTls && !tlsAtivo)
                    anuncios.Add("STARTTLS");
                if (AnunciaAuth && (Tls == ModoTls.Nenhum || tlsAtivo))
                    anuncios.Add("AUTH PLAIN");
                await canal.EscreverMultilinhaAsync(250, anuncios, ct);
            }
            else if (comando.StartsWith("STARTTLS"))
            {
                await canal.EscreverAsync("220 2.0.0 Pronto para TLS", ct);
                fluxo = await ElevarParaTlsAsync(fluxo, ct);
                canal = new Canal(fluxo);
                tlsAtivo = true;
            }
            else if (comando.StartsWith("AUTH PLAIN"))
            {
                var resposta = RespostaAuth;
                if (resposta.StartsWith("235"))
                    usuario = DecodificarUsuarioPlain(linha);
                await canal.EscreverAsync(resposta, ct);
            }
            else if (comando.StartsWith("MAIL FROM"))
            {
                if (ExigeAuth && usuario is null)
                {
                    await canal.EscreverAsync("530 5.7.0 Autenticacao necessaria", ct);
                    continue;
                }

                remetente = ExtrairEndereco(linha);
                destinatarios = [];
                await canal.EscreverAsync("250 2.1.0 OK", ct);
            }
            else if (comando.StartsWith("RCPT TO"))
            {
                Interlocked.Increment(ref _rcpts);
                if (RespostaRcpt.StartsWith("25"))
                    destinatarios.Add(ExtrairEndereco(linha));
                await canal.EscreverAsync(RespostaRcpt, ct);
            }
            else if (comando == "DATA")
            {
                await canal.EscreverAsync("354 Envie a mensagem, termine com <CRLF>.<CRLF>", ct);
                _dataIniciado.TrySetResult();
                if (TravarAposData)
                {
                    await DescartarAteFecharAsync(fluxo, ct);
                    return;
                }

                var bruto = await canal.LerDadosAsync(ct);
                if (bruto is null)
                    return;

                _mensagens.Enqueue(new MensagemRecebida(remetente, destinatarios.ToArray(), bruto, usuario));
                await canal.EscreverAsync("250 2.0.0 Aceita para entrega", ct);
            }
            else if (comando is "RSET" or "NOOP")
            {
                await canal.EscreverAsync("250 2.0.0 OK", ct);
            }
            else if (comando == "QUIT")
            {
                await canal.EscreverAsync("221 2.0.0 Tchau", ct);
                return;
            }
            else
            {
                await canal.EscreverAsync("502 5.5.2 Comando nao implementado", ct);
            }
        }
    }

    private async Task<Stream> ElevarParaTlsAsync(Stream fluxo, CancellationToken ct)
    {
        var tls = new SslStream(fluxo, leaveInnerStreamOpen: false);
        await tls.AuthenticateAsServerAsync(
            new SslServerAuthenticationOptions
            {
                ServerCertificate = _certificado,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            },
            ct);
        return tls;
    }

    private static async Task DescartarAteFecharAsync(Stream fluxo, CancellationToken ct)
    {
        var lixo = new byte[1024];
        while (await fluxo.ReadAsync(lixo, ct) > 0)
        {
        }
    }

    private static string ExtrairEndereco(string linha)
    {
        var abre = linha.IndexOf('<');
        var fecha = linha.IndexOf('>');
        return abre >= 0 && fecha > abre ? linha[(abre + 1)..fecha] : string.Empty;
    }

    private static string? DecodificarUsuarioPlain(string linha)
    {
        var partes = linha.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (partes.Length < 3)
            return null;
        var credencial = Encoding.UTF8.GetString(Convert.FromBase64String(partes[2])).Split('\0');
        return credencial.Length >= 3 ? credencial[1] : null;
    }

    public async ValueTask DisposeAsync()
    {
        await _parar.CancelAsync();
        _listener.Stop();
        if (_aceitador is not null)
            await _aceitador;
        try
        {
            await Task.WhenAll(_atendimentos).WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
        {
        }

        _certificado?.Dispose();
        _parar.Dispose();
    }

    private sealed class Canal(Stream fluxo)
    {
        private readonly StreamReader _leitor = new(fluxo, Encoding.UTF8, false, 4096, leaveOpen: true);
        private readonly StreamWriter _escritor = new(fluxo, new UTF8Encoding(false), 4096, leaveOpen: true)
        {
            NewLine = "\r\n",
            AutoFlush = true,
        };

        public async Task EscreverAsync(string linha, CancellationToken ct) =>
            await _escritor.WriteLineAsync(linha.AsMemory(), ct);

        public async Task EscreverMultilinhaAsync(int codigo, IReadOnlyList<string> linhas, CancellationToken ct)
        {
            for (var i = 0; i < linhas.Count; i++)
                await EscreverAsync($"{codigo}{(i == linhas.Count - 1 ? ' ' : '-')}{linhas[i]}", ct);
        }

        public async Task<string?> LerLinhaAsync(CancellationToken ct) => await _leitor.ReadLineAsync(ct);

        /// <summary>Le o corpo do DATA ate a linha so com ponto, desfazendo o dot-stuffing.</summary>
        public async Task<string?> LerDadosAsync(CancellationToken ct)
        {
            var corpo = new StringBuilder();
            while (true)
            {
                var linha = await _leitor.ReadLineAsync(ct);
                if (linha is null)
                    return null;
                if (linha == ".")
                    return corpo.ToString();
                corpo.Append(linha.StartsWith("..") ? linha[1..] : linha).Append("\r\n");
            }
        }
    }
}

/// <summary>Certificado autoassinado de curta duracao para o servidor falso (nunca sai do processo de teste).</summary>
internal static class CertificadoAutoassinado
{
    public static X509Certificate2 Criar()
    {
        using var chave = RSA.Create(2048);
        var pedido = new CertificateRequest("CN=localhost", chave, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        pedido.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        pedido.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
        pedido.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new("1.3.6.1.5.5.7.3.1") }, false));
        var nomes = new SubjectAlternativeNameBuilder();
        nomes.AddDnsName("localhost");
        nomes.AddIpAddress(IPAddress.Loopback);
        pedido.CertificateExtensions.Add(nomes.Build());

        using var autoassinado = pedido.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        // Recarregar do PFX: o SslStream do Windows nao usa a chave efemera do CreateSelfSigned.
        return X509CertificateLoader.LoadPkcs12(autoassinado.Export(X509ContentType.Pfx), null);
    }
}
