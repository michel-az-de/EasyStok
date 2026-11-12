using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using EasyStock.Api.Configuration;
using EasyStock.Api.Services;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Comanda;
using EasyStock.Application.UseCases.FeatureFlags;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Services;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;

namespace EasyStock.Api.IntegrationTests.Atendimento;

public sealed partial class PedidoConversaConcorrenciaTests
{
    [SkippableFact]
    public async Task R4_FixtureLocalComLoginReal()
    {
        var arquivo = Environment.GetEnvironmentVariable("EASYSTOK_R4_BROWSER_FIXTURE");
        Skip.If(string.IsNullOrWhiteSpace(arquivo), "Fixture opt-in: informe EASYSTOK_R4_BROWSER_FIXTURE.");
        Skip.If(!_isAvailable, "Docker/PostgreSQL indisponível.");
        var cloud = Substitute.For<IWhatsAppCloudClient>();
        cloud.EnviarTextoAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => new EnvioWhatsAppResult("wamid.r4.controlado." + Guid.NewGuid().ToString("N")));
        await using var factory = CriarFactory(Provedor(new())).WithWebHostBuilder(b =>
        {
            foreach (var flag in typeof(BackgroundJobOptions).GetProperties().Where(p => p.PropertyType == typeof(bool)))
                b.UseSetting(BackgroundJobOptions.SectionName + ":" + flag.Name, "false");
            b.ConfigureTestServices(s =>
            {
                s.RemoveAll<ICurrentUserAccessor>();
                s.AddScoped<ICurrentUserAccessor, CurrentUserAccessor>();
                s.AddAuthentication(o =>
                {
                    o.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                    o.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
                    o.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
                });
                // O startup captura a chave antes do ConfigureAppConfiguration da factory.
                // Alinha a validação real à chave fictícia usada pelo emissor real desta fixture.
                s.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
                {
                    o.TokenValidationParameters.ValidIssuer = "EasyStock";
                    o.TokenValidationParameters.ValidAudience = "EasyStock";
                    o.TokenValidationParameters.IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes("EasyStock-Test-SuperSecretKey-Min32Chars!!"));
                });
                s.AddSingleton(cloud);
                s.AddSingleton<IAgenteLlmClient, R4LlmControlado>();
                // Só o servidor HTTP roda. Nenhum disparador, coleta ou consumidor de fila fica ativo neste laboratório.
                foreach (var worker in s.Where(d => d.ServiceType == typeof(IHostedService)
                    && d.ImplementationType?.FullName?.StartsWith("Microsoft.AspNetCore.Hosting.", StringComparison.Ordinal) != true).ToArray())
                    s.Remove(worker);
            });
        });
        factory.UseKestrel(0);
        using var client = factory.CreateClient();
        var cenario = await SemearAsync(factory, "casa-da-baba", EasyStock.Application.Common.HorarioBrasil.Hoje().AddDays(1));
        var outraEmpresa = Guid.NewGuid();
        var identidades = new[]
        {
            new IdentidadeR4(UsuarioId, EmpresaId, "Dona R4", "dona.r4@fixture.test", "R4-Dona-2026!", "Dona"),
            new IdentidadeR4(Guid.NewGuid(), EmpresaId, "Atendimento R4", "atendimento.r4@fixture.test", "R4-Atende-2026!", "Atendimento"),
            new IdentidadeR4(Guid.NewGuid(), outraEmpresa, "Dona de outra empresa", "outra.r4@fixture.test", "R4-Outra-2026!", "Dona")
        };
        Guid conversaSemCadastroId;
        Guid conversaConferenciaId;
        Guid conversaOutraEmpresaId;
        Guid janelaAlternativaId;
        var produtos = new List<object>();
        using (var scope = factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetCurrentTenant(EmpresaId);
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            using var bypass = db.UseRowLevelSecurityBypass();
            var agora = DateTime.UtcNow;
            db.Empresas.Add(new Empresa { Id = outraEmpresa, Nome = "Controle isolado R4", Documento = outraEmpresa.ToString("N")[..14], CriadoEm = agora, AlteradoEm = agora });
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            foreach (var identidade in identidades)
            {
                db.Usuarios.Add(new Usuario { Id = identidade.UsuarioId, Nome = identidade.Nome, Email = identidade.Email,
                    SenhaHash = hasher.Hash(identidade.Senha), Ativo = true, EmailConfirmado = true, CriadoEm = agora, AlteradoEm = agora });
                db.UsuariosEmpresas.Add(new UsuarioEmpresa { Id = Guid.NewGuid(), UsuarioId = identidade.UsuarioId,
                    EmpresaId = identidade.EmpresaId, Ativo = true, CriadoEm = agora });
                var modelo = PerfisCasaDaBaba.Iniciais.Single(p => p.Nome == identidade.Perfil);
                var perfil = new Perfil { Id = Guid.NewGuid(), EmpresaId = identidade.EmpresaId, Nome = modelo.Nome,
                    Nivel = modelo.Nivel, ModuloInicial = modelo.ModuloInicial, PermissoesExplicitas = true, CriadoEm = agora };
                foreach (var permissao in modelo.Permissoes)
                    perfil.Permissoes.Add(new PerfilPermissao { Id = Guid.NewGuid(), PerfilId = perfil.Id, Permissao = permissao });
                db.Perfis.Add(perfil);
                db.UsuariosPerfis.Add(new UsuarioPerfil { Id = Guid.NewGuid(), UsuarioId = identidade.UsuarioId,
                    EmpresaId = identidade.EmpresaId, PerfilId = perfil.Id, AtribuidoEm = agora });
            }
            var clienteOriginal = await db.Clientes.SingleAsync(c => c.Id == cenario.ClienteId);
            clienteOriginal.Nome = "Ana Pereira";
            clienteOriginal.Ativo = true;
            clienteOriginal.Telefone = "+5511999998888";
            var conversa = await db.AtendimentoConversas.SingleAsync(c => c.Id == cenario.ConversaId);
            conversa.AtualizarContatoNome(clienteOriginal.Nome);
            conversa.Assumir(agora, UsuarioId);
            conversa.RegistrarEntrada(agora);
            db.AtendimentoMensagens.Add(Mensagem.Entrada(EmpresaId, conversa.Id, agora, TipoConteudoMensagem.Texto,
                "Meu nome é Ana Pereira. Meu telefone é 11999998888 e meu email é ana@fixture.test.", "wamid.r4.ana"));
            foreach (var nome in new[] { "Bruno Pereira", "Carla Pereira" })
            {
                var cliente = Cliente.Criar(EmpresaId, nome);
                cliente.Telefone = clienteOriginal.Telefone;
                db.Clientes.Add(cliente);
            }
            var semCadastro = Conversa.Abrir(EmpresaId, "5511912345678", agora, "Novo contato");
            semCadastro.Assumir(agora, UsuarioId);
            semCadastro.RegistrarEntrada(agora);
            db.AtendimentoConversas.Add(semCadastro);
            db.AtendimentoMensagens.Add(Mensagem.Entrada(EmpresaId, semCadastro.Id, agora, TipoConteudoMensagem.Texto,
                "Meu nome é Ana Pereira. Meu telefone é 11999998888 e meu email é ana@fixture.test.", "wamid.r4.sem-cadastro"));
            conversaSemCadastroId = semCadastro.Id;
            var conferencia = Conversa.Abrir(EmpresaId, "5511912345679", agora, "Conferência R4", clienteOriginal.Id);
            conferencia.Assumir(agora, UsuarioId);
            conferencia.RegistrarEntrada(agora);
            db.AtendimentoConversas.Add(conferencia);
            db.AtendimentoMensagens.Add(Mensagem.Entrada(EmpresaId, conferencia.Id, agora, TipoConteudoMensagem.Texto,
                "Mensagem sintética recebida para conferir as exceções R4.", "wamid.r4.conferencia"));
            conversaConferenciaId = conferencia.Id;
            var estrangeiro = Cliente.Criar(outraEmpresa, "Cliente isolado R4");
            estrangeiro.Telefone = clienteOriginal.Telefone;
            db.Clientes.Add(estrangeiro);
            var conversaEstrangeira = Conversa.Abrir(outraEmpresa, "5511999998888", agora, estrangeiro.Nome, estrangeiro.Id);
            db.AtendimentoConversas.Add(conversaEstrangeira);
            conversaOutraEmpresaId = conversaEstrangeira.Id;
            var categoria = new Categoria { Id = Guid.NewGuid(), EmpresaId = EmpresaId, Nome = "Massas R4", CriadoEm = agora, AlteradoEm = agora };
            db.Categorias.Add(categoria);
            var loja = Loja.Criar(EmpresaId, "Cozinha R4");
            db.Lojas.Add(loja);
            var storefront = await db.Storefronts.SingleAsync(x => x.EmpresaId == EmpresaId);
            var janelaAlternativa = JanelaEntrega.Criar(storefront.Id, (int)cenario.DataEntrega.DayOfWeek,
                new TimeOnly(13, 0), new TimeOnly(16, 0), 10, "Tarde R4");
            db.JanelasEntrega.Add(janelaAlternativa);
            janelaAlternativaId = janelaAlternativa.Id;
            foreach (var nome in new[] { "Lasanha bolonhesa R4", "Lasanha de legumes R4", "Lasanha da prova de falta R4" })
            {
                var saldo = nome == "Lasanha da prova de falta R4" ? 1 : 8;
                var produto = new Produto { Id = Guid.NewGuid(), EmpresaId = EmpresaId, CategoriaId = categoria.Id,
                    Nome = nome, Status = StatusProduto.Ativo, PrecoReferencia = Dinheiro.FromDecimal(32m), CriadoEm = agora, AlteradoEm = agora };
                db.Produtos.Add(produto);
                var item = new ItemEstoque { Id = Guid.NewGuid(), EmpresaId = EmpresaId, ProdutoId = produto.Id, LojaId = loja.Id,
                    QuantidadeInicial = Quantidade.From(saldo), QuantidadeAtual = Quantidade.From(saldo), CustoUnitario = Dinheiro.FromDecimal(12m),
                    Status = StatusItemEstoque.Ok, EntradaEm = agora, CriadoEm = agora, AlteradoEm = agora };
                db.ItensEstoque.Add(item);
                var cardapio = CardapioItem.CriarAvulso(storefront.Id, nome, 32m, categoria.Nome);
                cardapio.VincularProduto(produto.Id);
                cardapio.TornarVisivel();
                db.CardapioItens.Add(cardapio);
                produtos.Add(new { produtoId = produto.Id, itemEstoqueId = item.Id, cardapioItemId = cardapio.Id, nome, saldo });
            }
            db.Fornecedores.Add(Fornecedor.Criar(EmpresaId, "Fornecedor R4"));
            db.TenantFeatureFlags.Add(TenantFeatureFlag.Criar(EmpresaId, FeatureCatalogo.ModuloAtendimento, true, identidades[0].Email));
            await db.SaveChangesAsync();
        }
        // Prova a autenticação da fixture: o header não forja claims e acesso anônimo é recusado.
        using (var anonimo = await client.GetAsync("api/auth/me/modulos")) Assert.Equal(HttpStatusCode.Unauthorized, anonimo.StatusCode);
        foreach (var identidade in identidades)
        {
            using var login = await client.PostAsJsonAsync("api/auth/login", new { identidade.Email, identidade.Senha, identidade.EmpresaId });
            Assert.True(login.IsSuccessStatusCode, $"Login de {identidade.Perfil}: {(int)login.StatusCode} {await login.Content.ReadAsStringAsync()}");
            using var json = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.RootElement.GetProperty("data").GetProperty("token").GetString());
            using var me = await client.GetAsync("api/auth/me/modulos");
            Assert.True(me.IsSuccessStatusCode, $"JWT de {identidade.Perfil}: {(int)me.StatusCode} {await me.Content.ReadAsStringAsync()}");
            client.DefaultRequestHeaders.Authorization = null;
        }
        var destino = Path.GetFullPath(arquivo!);
        Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
        await File.WriteAllTextAsync(destino, JsonSerializer.Serialize(new
        {
            api = client.BaseAddress!.ToString().TrimEnd('/'), empresaId = EmpresaId, outraEmpresaId = outraEmpresa,
            conversaId = cenario.ConversaId, clienteId = cenario.ClienteId, conversaSemCadastroId, conversaConferenciaId, conversaOutraEmpresaId,
            janelaId = cenario.JanelaId, janelaAlternativaId, dataEntrega = cenario.DataEntrega, produtos, produtoExcecao = produtos[2], identidades,
            autenticacao = "POST api/auth/login, JWT Bearer e CurrentUserAccessor reais",
            provedores = "MP, Meta e LLM controlados; nenhuma homologação externa; workers desativados"
        }, ComandaClienteUseCase.Json));
        using var limite = new CancellationTokenSource(TimeSpan.FromMinutes(60));
        while (!File.Exists(destino + ".stop") && !limite.IsCancellationRequested) await Task.Delay(500);
    }

    private sealed record IdentidadeR4(Guid UsuarioId, Guid EmpresaId, string Nome, string Email, string Senha, string Perfil);

    private sealed class R4LlmControlado : IAgenteLlmClient
    {
        private readonly LlmBrowserControlado _operacao = new();
        public bool Disponivel => true;
        public Task<RespostaLlm> EnviarAsync(RequisicaoLlm requisicao, CancellationToken ct = default)
        {
            if (!requisicao.Ferramentas.Any(f => f.Nome == "propor_cadastro")) return _operacao.EnviarAsync(requisicao, ct);
            var texto = requisicao.Mensagens.Last().Conteudo.OfType<BlocoTextoLlm>().Single().Texto;
            using var fontes = JsonDocument.Parse(texto);
            var campos = new List<object>();
            foreach (var fonte in fontes.RootElement.EnumerateArray())
            {
                var evidencia = fonte.GetProperty("texto").GetString()!;
                if (!evidencia.StartsWith("Meu nome é Ana Pereira.", StringComparison.Ordinal)) continue;
                foreach (var (campo, valor) in new[] { ("nome", "Ana Pereira"), ("telefone", "11999998888"), ("email", "ana@fixture.test") })
                    campos.Add(new { campo, valor, mensagemId = fonte.GetProperty("mensagemId").GetGuid(), evidencia });
                break;
            }
            return Task.FromResult(new RespostaLlm(RespostaLlm.StopToolUse,
                [new BlocoUsoFerramentaLlm("r4.cadastro", "propor_cadastro", JsonSerializer.SerializeToElement(new { campos }))], 1, 1));
        }
    }
}
