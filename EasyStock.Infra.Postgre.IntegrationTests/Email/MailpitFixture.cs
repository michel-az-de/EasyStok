using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace EasyStock.Infra.Postgre.IntegrationTests.Email;

/// <summary>
/// Captador de e-mail de verdade para os testes do <c>SmtpEmailService</c> (N3, #1351): um Mailpit em container, com o
/// SMTP na 1025 e a API HTTP na 8025. O teste envia pelo servico real e le a mensagem de volta pela API do Mailpit
/// (<c>GET /api/v1/messages</c>, <c>GET /api/v1/message/{id}</c>, <c>DELETE /api/v1/messages</c>), entao o que chega e
/// o que o MailKit de fato mandou, nao o que um mock acha que mandou.
///
/// A tag e fixa (nunca <c>latest</c>) e e a mesma do <c>docker-compose.local.yml</c>. Sem Docker a fixture marca
/// <see cref="IsAvailable"/> falso e os testes pulam VISIVEIS (<c>Skip.If</c>, ADR-0023); na CI o
/// <see cref="MailpitCanaryTests"/> transforma isso em vermelho.
/// </summary>
public class MailpitFixture : IAsyncLifetime
{
    public const string Imagem = "axllent/mailpit:v1.31.3";

    private const int PortaSmtpDoContainer = 1025;
    private const int PortaHttpDoContainer = 8025;

    private static readonly TimeSpan EsperaPadrao = TimeSpan.FromSeconds(15);

    private readonly HttpClient _http = new();
    private IContainer? _container;

    public bool IsAvailable { get; private set; }

    public string? UnavailableReason { get; private set; }

    public string Host => Container.Hostname;

    public int PortaSmtp => Container.GetMappedPublicPort(PortaSmtpDoContainer);

    private IContainer Container => _container ?? throw new InvalidOperationException("Mailpit de teste indisponivel.");

    /// <summary>Ponto de extensao das fixtures com TLS (certificado e exigencia de STARTTLS ou TLS implicito).</summary>
    protected virtual ContainerBuilder Configurar(ContainerBuilder construtor) => construtor;

    public async Task InitializeAsync()
    {
        try
        {
            _container = Configurar(new ContainerBuilder(Imagem))
                .WithPortBinding(PortaSmtpDoContainer, true)
                .WithPortBinding(PortaHttpDoContainer, true)
                .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r
                    .ForPort(PortaHttpDoContainer)
                    .ForPath("/livez")))
                .Build();

            await _container.StartAsync();
            _http.BaseAddress = new Uri($"http://{Container.Hostname}:{Container.GetMappedPublicPort(PortaHttpDoContainer)}");
            IsAvailable = true;
        }
        catch (Exception ex)
        {
            // Docker ausente, sem rede para baixar a imagem ou container que nao subiu: vira SKIP local e vermelho
            // na CI (MailpitCanaryTests), nunca um erro de fixture que derruba o resto da suite.
            IsAvailable = false;
            UnavailableReason = ex.Message;
        }
    }

    public async Task DisposeAsync()
    {
        _http.Dispose();
        if (_container is not null)
            await _container.DisposeAsync();
    }

    /// <summary>Apaga tudo antes de cada teste: o container e compartilhado e a ordem dos testes nao importa.</summary>
    public async Task LimparAsync()
    {
        using var resposta = await _http.DeleteAsync("/api/v1/messages");
        resposta.EnsureSuccessStatusCode();
    }

    public async Task<int> ContarAsync()
    {
        using var lista = JsonDocument.Parse(await _http.GetStringAsync("/api/v1/messages?limit=1"));
        return lista.RootElement.GetProperty("total").GetInt32();
    }

    /// <summary>Espera chegar pelo menos <paramref name="quantidade"/> mensagens e devolve todas, ja com o detalhe.</summary>
    public async Task<IReadOnlyList<MensagemCapturada>> AguardarAsync(int quantidade, TimeSpan? limite = null)
    {
        var relogio = Stopwatch.StartNew();
        var teto = limite ?? EsperaPadrao;
        while (true)
        {
            using var lista = JsonDocument.Parse(await _http.GetStringAsync("/api/v1/messages?limit=200"));
            var ids = lista.RootElement.GetProperty("messages").EnumerateArray()
                .Select(m => m.GetProperty("ID").GetString()!)
                .ToList();

            if (ids.Count >= quantidade)
            {
                var detalhadas = new List<MensagemCapturada>();
                foreach (var id in ids)
                    detalhadas.Add(await LerAsync(id));
                return detalhadas;
            }

            if (relogio.Elapsed > teto)
                throw new TimeoutException($"Chegaram {ids.Count} de {quantidade} mensagem(ns) no Mailpit em {teto.TotalSeconds:F0}s.");

            await Task.Delay(100);
        }
    }

    public async Task<MensagemCapturada> LerAsync(string id)
    {
        using var detalhe = JsonDocument.Parse(await _http.GetStringAsync($"/api/v1/message/{id}"));
        using var cabecalhos = JsonDocument.Parse(await _http.GetStringAsync($"/api/v1/message/{id}/headers"));
        var raiz = detalhe.RootElement;

        return new MensagemCapturada(
            id,
            raiz.GetProperty("From").GetProperty("Address").GetString()!,
            raiz.GetProperty("From").GetProperty("Name").GetString(),
            raiz.GetProperty("To").EnumerateArray().Select(p => p.GetProperty("Address").GetString()!).ToList(),
            raiz.GetProperty("Subject").GetString()!,
            raiz.GetProperty("HTML").GetString()!,
            raiz.GetProperty("Text").GetString()!,
            raiz.TryGetProperty("Username", out var usuario) && !string.IsNullOrEmpty(usuario.GetString()) ? usuario.GetString() : null,
            cabecalhos.RootElement.EnumerateObject()
                .ToDictionary(
                    p => p.Name,
                    p => p.Value.EnumerateArray().Select(v => v.GetString()!).ToArray(),
                    StringComparer.OrdinalIgnoreCase),
            raiz.GetProperty("Attachments").EnumerateArray()
                .Select(a => new AnexoCapturado(
                    a.GetProperty("PartID").GetString()!,
                    a.GetProperty("FileName").GetString()!,
                    a.GetProperty("ContentType").GetString()!,
                    a.GetProperty("Size").GetInt64()))
                .ToList());
    }

    public async Task<byte[]> BaixarAnexoAsync(string idDaMensagem, string idDaParte) =>
        await _http.GetByteArrayAsync($"/api/v1/message/{idDaMensagem}/part/{idDaParte}");
}

/// <summary>Mensagem como o Mailpit a guardou: o que o servidor SMTP de fato recebeu.</summary>
public sealed record MensagemCapturada(
    string Id,
    string DeEndereco,
    string? DeNome,
    IReadOnlyList<string> Para,
    string Assunto,
    string Html,
    string Texto,
    string? UsuarioAutenticado,
    IReadOnlyDictionary<string, string[]> Cabecalhos,
    IReadOnlyList<AnexoCapturado> Anexos)
{
    public string? Cabecalho(string nome) => Cabecalhos.TryGetValue(nome, out var valores) ? valores.FirstOrDefault() : null;
}

public sealed record AnexoCapturado(string IdDaParte, string NomeDoArquivo, string TipoDeConteudo, long Tamanho);

/// <summary>
/// Mailpit que exige STARTTLS (o analogo da porta 587), com certificado autoassinado gerado na hora. Estas duas
/// fixtures com TLS existem para provar o MailKit contra um servidor de verdade; o servidor SMTP falso dos testes
/// unitarios ja cobre os mesmos caminhos sem Docker (e a decisao de modo por porta fica no SmtpOpcoesTests).
/// </summary>
public sealed class MailpitStartTlsFixture : MailpitFixture
{
    protected override ContainerBuilder Configurar(ContainerBuilder construtor) =>
        CertificadoDoMailpit.Aplicar(construtor).WithEnvironment("MP_SMTP_REQUIRE_STARTTLS", "true");
}

/// <summary>Mailpit com TLS implicito na conexao (o analogo da porta 465, SMTPS).</summary>
public sealed class MailpitTlsImplicitoFixture : MailpitFixture
{
    protected override ContainerBuilder Configurar(ContainerBuilder construtor) =>
        CertificadoDoMailpit.Aplicar(construtor).WithEnvironment("MP_SMTP_REQUIRE_TLS", "true");
}

internal static class CertificadoDoMailpit
{
    /// <summary>Gera o certificado, entrega certificado e chave ao container e aceita qualquer credencial no AUTH.</summary>
    public static ContainerBuilder Aplicar(ContainerBuilder construtor)
    {
        using var chave = RSA.Create(2048);
        var pedido = new CertificateRequest("CN=localhost", chave, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var nomes = new SubjectAlternativeNameBuilder();
        nomes.AddDnsName("localhost");
        nomes.AddIpAddress(IPAddress.Loopback);
        pedido.CertificateExtensions.Add(nomes.Build());
        using var certificado = pedido.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));

        return construtor
            .WithResourceMapping(Encoding.ASCII.GetBytes(certificado.ExportCertificatePem()), "/certs/cert.pem")
            .WithResourceMapping(Encoding.ASCII.GetBytes(chave.ExportPkcs8PrivateKeyPem()), "/certs/key.pem")
            .WithEnvironment("MP_SMTP_TLS_CERT", "/certs/cert.pem")
            .WithEnvironment("MP_SMTP_TLS_KEY", "/certs/key.pem")
            .WithEnvironment("MP_SMTP_AUTH_ACCEPT_ANY", "true");
    }
}

[CollectionDefinition("Mailpit")]
public sealed class MailpitCollection : ICollectionFixture<MailpitFixture>
{
}
