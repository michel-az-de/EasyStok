using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using DotNet.Testcontainers.Builders;
using EasyStock.Application.Ports.Output;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Api.IntegrationTests.Atendimento;

/// <summary>
/// F08 item 1 (#1238): clique duplo no "Gerar pedido" do console (ou console + ferramenta do agente)
/// não pode criar dois pedidos na mesma conversa. Postgres real, requisições HTTP em paralelo.
/// </summary>
public sealed partial class PedidoConversaConcorrenciaTests : IAsyncLifetime
{
    private const int Paralelas = 4;
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly Guid UsuarioId = Guid.NewGuid();

    private PostgreSqlContainer? _pg;
    private bool _isAvailable;

    public async Task InitializeAsync()
    {
        try
        {
            _pg = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("easystock_pedido_conversa_tests")
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
    public async Task GerarPedido_ChamadasParalelas_CriaUmUnicoPedido()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");

        await using var factory = CriarFactory();
        using var client = factory.CreateClient();
        var cenario = await SemearAsync(factory);

        var corpo = new
        {
            itens = new[] { new { cardapioItemId = cenario.CardapioItemId, qtd = 1 } },
            janelaId = cenario.JanelaId,
            dataEntrega = cenario.DataEntrega.ToString("yyyy-MM-dd"),
            forma = "na_entrega",
        };

        var respostas = await Task.WhenAll(Enumerable.Range(0, Paralelas).Select(_ =>
            client.PostAsJsonAsync($"/api/atendimento/conversas/{cenario.ConversaId}/pedido", corpo)));

        respostas.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1, "só uma chamada gera o pedido");
        respostas.Count(r => r.StatusCode == HttpStatusCode.Conflict)
            .Should().Be(Paralelas - 1, "as outras recusam: a conversa já tem pedido em andamento");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        var pedidos = await db.Pedidos.IgnoreQueryFilters()
            .Where(p => p.EmpresaId == EmpresaId && p.ClienteId == cenario.ClienteId)
            .Select(p => p.Id)
            .ToListAsync();
        pedidos.Should().ContainSingle();
        var conversa = await db.AtendimentoConversas.IgnoreQueryFilters().SingleAsync(c => c.Id == cenario.ConversaId);
        conversa.PedidoEmAndamentoId.Should().Be(pedidos[0]);
    }

    private sealed record Cenario(Guid ConversaId, Guid ClienteId, Guid CardapioItemId, Guid JanelaId, DateOnly DataEntrega);

    private static async Task<Cenario> SemearAsync(WebApplicationFactory<Program> factory, string? slug = null, DateOnly? dataEntrega = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        var agora = DateTime.UtcNow;
        db.SetMobileTenantContext(EmpresaId);

        db.Empresas.Add(new Empresa
        {
            Id = EmpresaId,
            Nome = "Empresa Pedido Conversa",
            Documento = EmpresaId.ToString("N")[..14],
            CriadoEm = agora,
            AlteradoEm = agora,
        });

        var storefront = StorefrontEntity.Criar(EmpresaId, slug ?? $"sf-conv-{Guid.NewGuid():N}"[..20], "SF Conversa", 0m);
        storefront.Ativar();
        db.Storefronts.Add(storefront);

        // Dia em Brasília: entre 21 h e meia-noite a data UTC já é amanhã, e a encomenda +7 empurrada mais 7 dias caía fora
        // do limite de 14 dias do site (#1961, Comanda_LeadCadastraEnderecoEEntregaForaDaAreaVaiParaHumano).
        var dia = dataEntrega ?? EasyStock.Application.Common.HorarioBrasil.Hoje().AddDays(7);
        var janela = JanelaEntrega.Criar(storefront.Id, (int)dia.DayOfWeek,
            new TimeOnly(9, 0), new TimeOnly(12, 0), 10, "Manhã");
        db.JanelasEntrega.Add(janela);

        db.FreteZonas.Add(FreteZona.CriarPorCep(storefront.Id, "Centro", "01000000", "01999999", 5m, 60));

        var item = CardapioItem.CriarAvulso(storefront.Id, "Brigadeiro", 10m);
        item.TornarVisivel();
        db.CardapioItens.Add(item);

        var cliente = new Cliente { Id = Guid.NewGuid(), EmpresaId = EmpresaId, Nome = "Maria", Telefone = "11999998888" };
        cliente.Enderecos.Add(new ClienteEndereco
        {
            Id = Guid.NewGuid(),
            ClienteId = cliente.Id,
            Logradouro = "Rua A",
            Numero = "10",
            Cep = "01310100",
            Bairro = "Centro",
            Cidade = "São Paulo",
            Estado = "SP",
            Padrao = true,
            CriadoEm = agora,
            AlteradoEm = agora,
        });
        db.Clientes.Add(cliente);

        var conversa = Conversa.Abrir(EmpresaId, "5511999998888", agora, "Maria", cliente.Id);
        db.AtendimentoConversas.Add(conversa);

        // #1889: o agente automático nasce desligado; estes cenários exercitam a loja que o ligou no console.
        var atendimento = ConfiguracaoAtendimento.CriarPadrao(EmpresaId);
        atendimento.Ativo = true;
        db.ConfiguracoesAtendimento.Add(atendimento);

        await db.SaveChangesAsync();
        return new Cenario(conversa.Id, cliente.Id, item.Id, janela.Id, dia);
    }

    private WebApplicationFactory<Program> CriarFactory(EasyStock.Application.Ports.Output.Pagamentos.IMercadoPagoClient? mp = null, NivelAcesso nivel = NivelAcesso.Operador)
    {
        if (_pg is null) throw new InvalidOperationException("Postgres test container indisponível.");

        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b =>
            {
                b.UseEnvironment("Development");
                // UseSetting entra antes do Program ler a configuração (o ConfigureAppConfiguration
                // chega tarde demais para o DatabaseProviderResolver do startup).
                b.UseSetting("Database:Provider", "PostgreSql");
                b.UseSetting("ConnectionStrings:DefaultConnection", _pg!.GetConnectionString());
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

                b.ConfigureTestServices(services =>
                {
                    var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(ICurrentUserAccessor));
                    if (descriptor is not null) services.Remove(descriptor);
                    services.AddSingleton<ICurrentUserAccessor>(new OperadoraStub(nivel));
                    if (mp is not null) services.AddSingleton(mp);

                    services
                        .AddAuthentication(opts =>
                        {
                            opts.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                            opts.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                        })
                        .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
                });
            });
    }

    private sealed class OperadoraStub(NivelAcesso nivel) : ICurrentUserAccessor
    {
        public Guid EmpresaId => PedidoConversaConcorrenciaTests.EmpresaId;
        public bool IsAuthenticated => true;
        public Guid UsuarioId => PedidoConversaConcorrenciaTests.UsuarioId;
        public NivelAcesso Nivel => nivel;
        public bool TemPermissao(Permissao permissao) => true;
    }

    private sealed class TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ICurrentUserAccessor currentUser)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "TestAuth";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, UsuarioId.ToString()),
                new Claim(ClaimTypes.Name, "Operadora"),
                new Claim("sub", UsuarioId.ToString()),
                new Claim("empresaId", currentUser.EmpresaId.ToString()),
                new Claim("nivel", currentUser.Nivel.ToString()),
            };
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
        }
    }
}
