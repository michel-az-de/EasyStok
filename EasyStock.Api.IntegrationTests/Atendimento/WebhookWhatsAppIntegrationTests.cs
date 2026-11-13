using System.Net;
using System.Security.Cryptography;
using System.Text;
using DotNet.Testcontainers.Builders;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.UseCases.FeatureFlags;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace EasyStock.Api.IntegrationTests.Atendimento;

/// <summary>
/// #2280: o POST de <c>/api/webhooks/whatsapp</c> com Postgres real. Até aqui só o use case era provado com
/// repositórios falsos; faltava a ponta HTTP: assinatura HMAC, resolução do tenant por <c>phone_number_id</c>
/// (coluna <c>Empresa.WhatsAppPhoneNumberId</c>) e a idempotência por wamid no banco.
/// Envio ao WhatsApp, LLM e fila são fakes: nada sai para a rede.
/// </summary>
public sealed class WebhookWhatsAppIntegrationTests : IAsyncLifetime
{
    private const string AppSecret = "segredo-app-meta-de-teste-descartavel";
    private const string PhoneNumberIdA = "100000000000001";
    private const string PhoneNumberIdB = "100000000000002";
    private const string ContatoWaId = "5511999990001";
    private static readonly Guid EmpresaA = Guid.NewGuid();
    private static readonly Guid EmpresaB = Guid.NewGuid();

    private PostgreSqlContainer? _pg;
    private bool _isAvailable;

    public async Task InitializeAsync()
    {
        try
        {
            _pg = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("easystock_webhook_whatsapp_tests")
                .WithUsername("postgres")
                .WithPassword("senha-de-teste-descartavel")
                .Build();
            await _pg.StartAsync();
            _isAvailable = true;
        }
        catch (DockerUnavailableException)
        {
            _isAvailable = false;
        }
    }

    public async Task DisposeAsync()
    {
        if (_pg is not null)
            await _pg.DisposeAsync();
    }

    [SkippableFact]
    public async Task Webhook_AssinaturaInvalidaOuAusente_Retorna403ENaoGravaNada()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory();
        using var client = factory.CreateClient();
        await SemearAsync(factory);
        var corpo = PayloadTexto("wamid.assinatura-1", PhoneNumberIdA, "oi, tem bolo?");

        using var ausente = await Postar(client, corpo, assinatura: null);
        using var errada = await Postar(client, corpo, Assinar(corpo, "outro-segredo"));
        using var adulterada = await Postar(client, corpo + " ", Assinar(corpo, AppSecret));

        ausente.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        errada.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        adulterada.StatusCode.Should().Be(HttpStatusCode.Forbidden, "a assinatura vale para o corpo exato");
        (await ContarEntradasAsync(factory, EmpresaA)).Should().Be(0);
        (await ContarConversasAsync(factory, EmpresaA)).Should().Be(0);
    }

    [SkippableFact]
    public async Task Webhook_TextoDeClienteNovo_GravaConversaEMensagemNoTenantDoPhoneNumberId()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory();
        using var client = factory.CreateClient();
        await SemearAsync(factory);
        var corpo = PayloadTexto("wamid.novo-1", PhoneNumberIdA, "oi, tem bolo?");

        using var resposta = await Postar(client, corpo, Assinar(corpo, AppSecret));

        resposta.StatusCode.Should().Be(HttpStatusCode.OK, await resposta.Content.ReadAsStringAsync());
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        var conversa = await db.AtendimentoConversas.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(c => c.EmpresaId == EmpresaA);
        conversa.Canal.Should().Be(CanalConversa.WhatsApp);
        conversa.ContatoIdExterno.Should().Be(ContatoWaId);
        var mensagem = await db.AtendimentoMensagens.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(m => m.EmpresaId == EmpresaA && m.Direcao == DirecaoMensagem.Entrada);
        mensagem.ConversaId.Should().Be(conversa.Id);
        mensagem.Direcao.Should().Be(DirecaoMensagem.Entrada);
        mensagem.Texto.Should().Be("oi, tem bolo?");
        mensagem.ExternoId.Should().Be("wamid.novo-1");
        (await ContarEntradasAsync(factory, EmpresaB)).Should().Be(0, "o outro tenant não recebe nada");
    }

    [SkippableFact]
    public async Task Webhook_ReentregaDoMesmoWamid_Retorna200EMantemUmaMensagemSo()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory();
        using var client = factory.CreateClient();
        await SemearAsync(factory);
        var corpo = PayloadTexto("wamid.reentrega-1", PhoneNumberIdA, "bom dia");

        using var primeira = await Postar(client, corpo, Assinar(corpo, AppSecret));
        using var segunda = await Postar(client, corpo, Assinar(corpo, AppSecret));

        primeira.StatusCode.Should().Be(HttpStatusCode.OK, await primeira.Content.ReadAsStringAsync());
        segunda.StatusCode.Should().Be(HttpStatusCode.OK, await segunda.Content.ReadAsStringAsync());
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        (await db.AtendimentoMensagens.IgnoreQueryFilters()
            .CountAsync(m => m.EmpresaId == EmpresaA && m.ExternoId == "wamid.reentrega-1")).Should().Be(1);
        (await ContarConversasAsync(factory, EmpresaA)).Should().Be(1);
    }

    [SkippableFact]
    public async Task Webhook_PhoneNumberIdDeOutroTenant_GravaSoNoDono()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory();
        using var client = factory.CreateClient();
        await SemearAsync(factory);
        var corpo = PayloadTexto("wamid.tenant-b-1", PhoneNumberIdB, "pedido da loja B");

        using var resposta = await Postar(client, corpo, Assinar(corpo, AppSecret));

        resposta.StatusCode.Should().Be(HttpStatusCode.OK, await resposta.Content.ReadAsStringAsync());
        (await ContarEntradasAsync(factory, EmpresaA)).Should().Be(0, "o phone_number_id é da empresa B");
        (await ContarEntradasAsync(factory, EmpresaB)).Should().Be(1);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        (await db.AtendimentoMensagens.IgnoreQueryFilters().SingleAsync(m => m.EmpresaId == EmpresaB && m.Direcao == DirecaoMensagem.Entrada))
            .ExternoId.Should().Be("wamid.tenant-b-1");
    }

    private static string PayloadTexto(string wamid, string phoneNumberId, string texto) => """
        {"entry":[{"changes":[{"field":"messages","value":{
            "messaging_product":"whatsapp",
            "metadata":{"phone_number_id":"__PHONE__"},
            "contacts":[{"profile":{"name":"Fulano"},"wa_id":"__WAID__"}],
            "messages":[{"from":"__WAID__","id":"__WAMID__","timestamp":"1700000000","type":"text","text":{"body":"__TEXTO__"}}]
        }}]}]}
        """
        .Replace("__PHONE__", phoneNumberId).Replace("__WAID__", ContatoWaId)
        .Replace("__WAMID__", wamid).Replace("__TEXTO__", texto);

    private static string Assinar(string corpo, string segredo) =>
        "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(segredo), Encoding.UTF8.GetBytes(corpo))).ToLowerInvariant();

    private static Task<HttpResponseMessage> Postar(HttpClient client, string corpo, string? assinatura)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/whatsapp")
        {
            Content = new StringContent(corpo, Encoding.UTF8, "application/json"),
        };
        if (assinatura is not null) req.Headers.Add("X-Hub-Signature-256", assinatura);
        return client.SendAsync(req);
    }

    private static async Task SemearAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        var agora = DateTime.UtcNow;
        foreach (var (id, nome, phone) in new[] { (EmpresaA, "Loja A", PhoneNumberIdA), (EmpresaB, "Loja B", PhoneNumberIdB) })
        {
            db.SetMobileTenantContext(id);
            var empresa = new Empresa
            {
                Id = id, Nome = nome, Documento = id.ToString("N")[..14], CriadoEm = agora, AlteradoEm = agora,
            };
            empresa.VincularWhatsApp(phone);
            db.Empresas.Add(empresa);
            db.TenantFeatureFlags.Add(TenantFeatureFlag.Criar(id, FeatureCatalogo.ModuloAtendimento, true, "teste@local.invalid"));
            await db.SaveChangesAsync();
        }
    }

    private static async Task<int> ContarEntradasAsync(WebApplicationFactory<Program> factory, Guid empresaId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        return await db.AtendimentoMensagens.IgnoreQueryFilters().CountAsync(m => m.EmpresaId == empresaId && m.Direcao == DirecaoMensagem.Entrada);
    }

    private static async Task<int> ContarConversasAsync(WebApplicationFactory<Program> factory, Guid empresaId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        return await db.AtendimentoConversas.IgnoreQueryFilters().CountAsync(c => c.EmpresaId == empresaId);
    }

    private WebApplicationFactory<Program> CriarFactory()
    {
        if (_pg is null) throw new InvalidOperationException("Postgres test container indisponível.");

        var llm = Substitute.For<IAgenteLlmClient>();
        llm.Disponivel.Returns(false);
        var cloud = Substitute.For<IWhatsAppCloudClient>();
        var fila = Substitute.For<IQueueService>();

        return new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development");
            b.UseSetting("Database:Provider", "PostgreSql");
            b.UseSetting("ConnectionStrings:DefaultConnection", _pg!.GetConnectionString());
            b.UseSetting("Notifications:WhatsApp:Provider", "meta");
            b.UseSetting("Notifications:WhatsApp:Meta:AccessToken", "fixture-controlada-sem-acesso-externo");
            b.UseSetting("Notifications:WhatsApp:Meta:AppSecret", AppSecret);
            b.UseSetting("Notifications:WhatsApp:Meta:VerifyToken", "fixture-controlada-sem-acesso-externo");
            b.ConfigureAppConfiguration((_, cfg) =>
            {
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:Provider"] = "PostgreSql",
                    ["ConnectionStrings:DefaultConnection"] = _pg!.GetConnectionString(),
                    ["ConnectionStrings:Redis"] = "localhost:6379",
                    ["Jwt:Issuer"] = "EasyStock",
                    ["Jwt:Audience"] = "EasyStock",
                    ["Jwt:SecretKey"] = "EasyStock-Test-SuperSecretKey-Min32Chars!!",
                    ["Jwt:ExpirationMinutes"] = "60",
                    ["Anthropic:Enabled"] = "false",
                    ["FileStorage:Provider"] = "Local",
                });
            });
            b.ConfigureTestServices(s =>
            {
                s.AddSingleton(llm);
                s.AddSingleton(cloud);
                s.AddSingleton(fila);
            });
        });
    }
}
