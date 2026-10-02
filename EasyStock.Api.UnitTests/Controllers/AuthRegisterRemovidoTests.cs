using System.Net;
using System.Net.Http.Json;
using EasyStock.Api.Controllers;
using EasyStock.Application.DependencyInjection;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Controllers;

/// <summary>
/// N0 (#1349): <c>POST /api/auth/register</c> era anônima, criava usuário sem empresa, mandava e-mail a
/// qualquer endereço (bastava o <c>BaseUrl</c> do corpo estar na allowlist) e enumerava conta: 200 para
/// e-mail novo, 409 para repetido. Sem a rota, a resposta é 404 igual à de qualquer rota inexistente, nos dois
/// casos, e nenhuma porta de usuário, e-mail ou banco é tocada.
///
/// Sobe o MVC de verdade (todos os controllers da Api, roteamento real, Kestrel em porta livre) com os
/// use cases de autenticação reais e as portas substituídas, no molde de
/// <see cref="OperacaoEventosControllerTests"/>: roda na CI, sem Docker nem Postgres.
/// </summary>
public class AuthRegisterRemovidoTests
{
    private const string OrigemConfiavel = "https://app.easystok.test";
    private const string EmailExistente = "existente@easystok.test";

    [Theory]
    [InlineData("novo@easystok.test")]
    [InlineData(EmailExistente)]
    public async Task Post_register_responde_404_e_nao_cria_usuario(string email)
    {
        var usuarios = Substitute.For<IUsuarioRepository>();
        usuarios.GetByEmailAsync(EmailExistente).Returns(Usuario.Criar("Existente", EmailExistente, "hash"));
        var emails = Substitute.For<IEmailService>();
        var unidadeDeTrabalho = Substitute.For<IUnitOfWork>();
        await using var api = await ApiEmMemoria.SubirAsync(usuarios, emails, unidadeDeTrabalho);
        using var asserts = new AssertionScope();

        var resposta = await api.Http.PostAsJsonAsync(
            "api/auth/register",
            new { nome = "Fulano", email, senha = "Senh@1234567", baseUrl = OrigemConfiavel });

        resposta.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "sem a rota, e-mail novo e e-mail ja cadastrado respondem igual e nao enumeram conta");
        Chamadas(usuarios).Should().BeEmpty("sem rota nao ha consulta nem gravacao em usuarios");
        Chamadas(emails).Should().BeEmpty("nenhum e-mail sai por uma rota que nao existe");
        Chamadas(unidadeDeTrabalho).Should().BeEmpty("nada e confirmado no banco");

        // Controle positivo: o host serve as demais rotas do AuthController. Sem ele, o 404 acima poderia ser
        // do host montado errado, e nao da rota removida.
        var controle = await api.Http.PostAsJsonAsync("api/auth/forgot-password", new { email = "ninguem@easystok.test" });
        controle.StatusCode.Should().Be(HttpStatusCode.Accepted, "forgot-password segue no ar (anti-enumeracao: sempre 202, N8)");
    }

    /// <summary>Nomes dos métodos que o substituto recebeu, para a falha mostrar tudo o que foi chamado.</summary>
    private static string Chamadas(object substituto) =>
        string.Join(", ", substituto.ReceivedCalls().Select(chamada => chamada.GetMethodInfo().Name));

    /// <summary>Kestrel em porta livre com o MVC real da Api e as portas de persistência e e-mail trocadas por substitutos.</summary>
    private sealed class ApiEmMemoria : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private ApiEmMemoria(WebApplication app, HttpClient http)
        {
            _app = app;
            Http = http;
        }

        public HttpClient Http { get; }

        public static async Task<ApiEmMemoria> SubirAsync(
            IUsuarioRepository usuarios, IEmailService emails, IUnitOfWork unidadeDeTrabalho)
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            // O e-mail so sai quando o BaseUrl do corpo bate com a allowlist: este e o vetor da N0.
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:TrustedLinkOrigins:0"] = OrigemConfiavel,
            });

            var senhas = Substitute.For<IPasswordHasher>();
            senhas.Hash(Arg.Any<string>()).Returns("hash-de-teste");
            var jwt = Substitute.For<EasyStock.Api.Services.IJwtTokenService>();

            builder.Services
                .AddEasyStockAuthenticationUseCases()
                .AddSingleton(usuarios)
                .AddSingleton(emails)
                .AddSingleton(unidadeDeTrabalho)
                .AddSingleton(senhas)
                .AddSingleton(jwt)
                .AddSingleton<EasyStock.Application.Ports.Output.IJwtTokenService>(jwt)
                .AddSingleton(Substitute.For<IAuditLogRepository>())
                .AddSingleton(Substitute.For<IRefreshTokenRepository>())
                .AddSingleton(Substitute.For<IResetTokenRepository>())
                .AddSingleton(Substitute.For<IEmailConfirmationTokenRepository>())
                .AddSingleton(Substitute.For<IUsuarioEmpresaRepository>())
                .AddSingleton(Substitute.For<ICacheService>()) // #1352: o RevogadorSessoes dos use cases reais apaga a chave de sessão
                // N4: a troca de contato enfileira eventos, e o export e a anonimizacao leem consentimentos e preferencias.
                .AddSingleton(Substitute.For<EasyStock.Application.Ports.Output.Notifications.INotificadorService>())
                .AddSingleton(Substitute.For<EasyStock.Application.Ports.Output.Notifications.IConsentimentoRepository>())
                .AddSingleton(Substitute.For<EasyStock.Application.Ports.Output.Notifications.IPreferenciaNotificacaoRepository>())
                .AddSingleton(Substitute.For<IEmpresaPadraoResolver>())
                .AddSingleton(Substitute.For<ITenantContextAccessor>())
                .AddSingleton(Substitute.For<ICurrentUserAccessor>());
            builder.Services.AddControllers().AddApplicationPart(typeof(AuthController).Assembly);

            var app = builder.Build();
            app.MapControllers();
            await app.StartAsync();

            var endereco = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
            return new ApiEmMemoria(app, new HttpClient { BaseAddress = new Uri(endereco) });
        }

        public async ValueTask DisposeAsync()
        {
            Http.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
