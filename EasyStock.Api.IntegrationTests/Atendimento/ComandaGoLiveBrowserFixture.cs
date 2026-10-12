using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using EasyStock.Api.Services;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.UseCases.Atendimento.ChatSite;
using EasyStock.Application.UseCases.Atendimento.Comanda;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Infra.Postgre.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace EasyStock.Api.IntegrationTests.Atendimento;

public sealed partial class PedidoConversaConcorrenciaTests
{
    private const string CabecalhoGoLive = "X-Comanda-Fixture-Atendente";
    private sealed record IdentidadeGoLive(Guid UsuarioId, Guid EmpresaId, string Nome, NivelAcesso Nivel)
    {
        public object Header => new { nome = CabecalhoGoLive, valor = UsuarioId.ToString() };
        public string[] Permissoes => Enum.GetValues<Permissao>().Where(p => EasyStock.Domain.Services.PoliticaPermissao.Tem(Nivel, [], p)).Select(p => p.ToString()).ToArray();
    }
    private sealed record ClienteGoLive(string Nome, Guid ClienteId, Guid ConversaId, string Token, long NumeroComanda, string Contato);
    private sealed record CadastroGoLive(Cenario Cenario, IReadOnlyList<ClienteGoLive> Clientes, ClienteGoLive OutroTenant);

    [SkippableFact]
    public async Task Comanda_FixtureGoLiveQuatroClientesDoisAtendentes()
    {
        var arquivo = Environment.GetEnvironmentVariable("EASYSTOK_COMANDA_GO_LIVE_FIXTURE");
        Skip.If(string.IsNullOrWhiteSpace(arquivo), "Ensaio opt-in requer EASYSTOK_COMANDA_GO_LIVE_FIXTURE explícito.");
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        var identidades = IdentidadesGoLive();
        await using var factory = CriarFactoryGoLive(identidades);
        factory.UseKestrel(0);
        using var client = factory.CreateClient();
        var cadastro = await SemearGoLive(factory, identidades);
        var dados = new { api = client.BaseAddress!.ToString().TrimEnd('/'), empresaId = EmpresaId,
            cadastro.Cenario.CardapioItemId, cadastro.Cenario.JanelaId, cadastro.Cenario.DataEntrega,
            clientes = cadastro.Clientes, atendentes = identidades.Take(2).ToArray(),
            outroTenant = new { atendente = identidades[2], cliente = cadastro.OutroTenant },
            limites = new { autenticacao = "TestAuth por cabeçalho; CurrentUserAccessor, perfis e autorização reais; não JWT",
                externos = "MP, Meta e LLM controlados; PostgreSQL, Kestrel, ferramentas, outbox e ledger reais",
                numeroComanda = "Estável pela conversa, sem reset automático diário" } };
        await File.WriteAllTextAsync(arquivo!, JsonSerializer.Serialize(dados, ComandaClienteUseCase.Json));
        using var limite = new CancellationTokenSource(TimeSpan.FromMinutes(60));
        while (!File.Exists(arquivo + ".stop") && !limite.IsCancellationRequested) await Task.Delay(500);
    }

    private static IdentidadeGoLive[] IdentidadesGoLive() =>
    [
        new(Guid.NewGuid(), EmpresaId, "Lia Atendimento", NivelAcesso.Operador),
        new(UsuarioId, EmpresaId, "Rita Gerente", NivelAcesso.Gerente),
        new(Guid.NewGuid(), Guid.NewGuid(), "Controle outro tenant", NivelAcesso.Gerente)
    ];

    private WebApplicationFactory<Program> CriarFactoryGoLive(IReadOnlyList<IdentidadeGoLive> identidades)
    {
        var mp = Provedor(new());
        var estornos = new EstornosBrowserControlados(mp);
        var llm = new LlmBrowserControlado();
        var mensagens = new ConcurrentQueue<string>();
        var detalhes = new ConcurrentQueue<MensagemCanalGoLive>();
        var cloud = Substitute.For<IWhatsAppCloudClient>();
        cloud.EnviarTextoAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(c =>
        {
            var id = $"wamid.fixture.{Guid.NewGuid():N}";
            mensagens.Enqueue(c.ArgAt<string>(1));
            detalhes.Enqueue(new(c.ArgAt<string>(0), c.ArgAt<string>(1), id, DateTime.UtcNow));
            return new EnvioWhatsAppResult(id);
        });
        return CriarFactory(mp).WithWebHostBuilder(b =>
        {
            b.UseSetting("Notifications:WhatsApp:Provider", "meta");
            foreach (var chave in new[] { "AccessToken", "AppSecret", "VerifyToken" })
                b.UseSetting($"Notifications:WhatsApp:Meta:{chave}", "fixture-controlada-sem-acesso-externo");
            b.ConfigureTestServices(s =>
            {
                s.RemoveAll<ICurrentUserAccessor>();
                s.AddScoped<ICurrentUserAccessor, CurrentUserAccessor>();
                s.AddSingleton<IReadOnlyDictionary<Guid, IdentidadeGoLive>>(identidades.ToDictionary(i => i.UsuarioId));
                s.AddAuthentication(o => { o.DefaultAuthenticateScheme = GoLiveAuthHandler.SchemeName; o.DefaultChallengeScheme = GoLiveAuthHandler.SchemeName; })
                    .AddScheme<AuthenticationSchemeOptions, GoLiveAuthHandler>(GoLiveAuthHandler.SchemeName, _ => { });
                s.AddSingleton(cloud);
                s.AddSingleton<IAgenteLlmClient>(llm);
                s.AddSingleton<Microsoft.AspNetCore.Hosting.IStartupFilter>(new ComandaBrowserBridge(mp, mensagens, estornos, llm, detalhes));
            });
        });
    }

    private static async Task<CadastroGoLive> SemearGoLive(WebApplicationFactory<Program> factory, IReadOnlyList<IdentidadeGoLive> identidades)
    {
        var c = await SemearAsync(factory, "casa-da-baba");
        await PrepararAvisos(factory, c.ConversaId);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        db.SetMobileTenantContext(EmpresaId);
        var agora = DateTime.UtcNow;
        var outro = identidades[2].EmpresaId;
        db.Empresas.Add(new Empresa { Id = outro, Nome = "Controle isolado", Documento = outro.ToString("N")[..14], CriadoEm = agora, AlteradoEm = agora });
        foreach (var identidade in identidades)
        {
            db.Usuarios.Add(new Usuario { Id = identidade.UsuarioId, Nome = identidade.Nome, Email = $"{identidade.UsuarioId:N}@fixture.test",
                SenhaHash = "fixture-sem-login-externo", Ativo = true, CriadoEm = agora, AlteradoEm = agora });
            db.UsuariosEmpresas.Add(new UsuarioEmpresa { Id = Guid.NewGuid(), UsuarioId = identidade.UsuarioId, EmpresaId = identidade.EmpresaId, Ativo = true, CriadoEm = agora });
            var perfil = new Perfil { Id = Guid.NewGuid(), EmpresaId = identidade.EmpresaId, Nome = identidade.Nivel.ToString(), Nivel = identidade.Nivel, CriadoEm = agora };
            db.Perfis.Add(perfil);
            db.UsuariosPerfis.Add(new UsuarioPerfil { Id = Guid.NewGuid(), UsuarioId = identidade.UsuarioId, EmpresaId = identidade.EmpresaId, PerfilId = perfil.Id, AtribuidoEm = agora });
        }
        var primeiro = await db.Clientes.SingleAsync(x => x.Id == c.ClienteId);
        primeiro.Nome = "Ana";
        var conversaInicial = await db.AtendimentoConversas.SingleAsync(x => x.Id == c.ConversaId);
        conversaInicial.AtualizarContatoNome("Ana");
        var conversas = new List<Conversa> { conversaInicial };
        foreach (var (nome, numero) in new[] { ("Bruno", "5511999998889"), ("Carla", "5511999998890"), ("Diego", "5511999998891"), ("Controle isolado", "5511999998892") })
        {
            var empresa = nome == "Controle isolado" ? outro : EmpresaId;
            var cliente = new Cliente { Id = Guid.NewGuid(), EmpresaId = empresa, Nome = nome, Telefone = numero[2..] };
            cliente.Enderecos.Add(new ClienteEndereco { Id = Guid.NewGuid(), ClienteId = cliente.Id, Logradouro = "Rua Fixture", Numero = "10",
                Cep = "01310100", Bairro = "Centro", Cidade = "São Paulo", Estado = "SP", Padrao = true, CriadoEm = agora, AlteradoEm = agora });
            db.Clientes.Add(cliente);
            conversas.Add(Conversa.Abrir(empresa, numero, agora, nome, cliente.Id));
        }
        foreach (var conversa in conversas)
        {
            if (conversa != conversaInicial) db.AtendimentoConversas.Add(conversa);
            conversa.RegistrarEntrada(agora);
            db.AtendimentoMensagens.Add(Mensagem.Entrada(conversa.EmpresaId, conversa.Id, agora, TipoConteudoMensagem.Texto,
                "Olá, quero montar minha comanda.", $"wamid.fixture.entrada.{Guid.NewGuid():N}"));
        }
        await db.SaveChangesAsync();
        var clientes = new List<ClienteGoLive>();
        foreach (var conversa in conversas)
        {
            var token = AcessoChatSite.NovoToken();
            db.LinksCardapioConversa.Add(LinkCardapioConversa.Gerar(conversa.EmpresaId, conversa.Id, AcessoChatSite.HashDoToken(token), agora));
            clientes.Add(new(conversa == conversaInicial ? "Ana" : conversa.ContatoNome!, conversa.ClienteId!.Value, conversa.Id, token,
                conversa.NumeroComanda, conversa.ContatoIdExterno));
        }
        await db.SaveChangesAsync();
        return new(c, clientes.Take(4).ToArray(), clientes[4]);
    }

    private sealed record MensagemCanalGoLive(string Destinatario, string Texto, string ProviderMensagemId, DateTime AceitoEm);

    private sealed class GoLiveAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder,
        IReadOnlyDictionary<Guid, IdentidadeGoLive> identidades) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "GoLiveTestAuth";
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var header = Request.Headers[CabecalhoGoLive].ToString();
            if (string.IsNullOrEmpty(header)) return Task.FromResult(AuthenticateResult.NoResult());
            if (!Guid.TryParse(header, out var id) || !identidades.TryGetValue(id, out var identidade))
                return Task.FromResult(AuthenticateResult.Fail("Identidade não cadastrada na fixture."));
            var claims = new[] { new Claim(ClaimTypes.NameIdentifier, id.ToString()), new Claim("sub", id.ToString()),
                new Claim(ClaimTypes.Name, identidade.Nome), new Claim("nome", identidade.Nome),
                new Claim("empresaId", identidade.EmpresaId.ToString()), new Claim("nivel", identidade.Nivel.ToString()),
                new Claim("iat", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture), ClaimValueTypes.Integer64) };
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName)), SchemeName)));
        }
    }
}
