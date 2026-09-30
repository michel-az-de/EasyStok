using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using EasyStock.Api.Authorization;
using EasyStock.Api.Controllers;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Operacao;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Operacao.Impressao;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Operacao;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Enums.Operacao;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Api.UnitTests.Controllers;

/// <summary>
/// S20 (#1156): canhoto em texto para ESC/POS (42 colunas, sem acentos), fila com retorno idempotente e
/// autenticação do bridge pela chave <c>X-Impressao-Api-Key</c>.
/// </summary>
public class ImpressaoControllerTests
{
    private static readonly DateTime Agora = new(2026, 9, 30, 15, 0, 0, DateTimeKind.Utc);

    private sealed class RelogioFixo(DateTime utc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utc, TimeSpan.Zero);
    }

    private sealed class Fakes
    {
        public Guid EmpresaId { get; } = Guid.NewGuid();
        public IPedidoRepository Pedidos { get; } = Substitute.For<IPedidoRepository>();
        public IClienteRepository Clientes { get; } = Substitute.For<IClienteRepository>();
        public IStorefrontRepository Storefronts { get; } = Substitute.For<IStorefrontRepository>();
        public ICardapioItemRepository Cardapio { get; } = Substitute.For<ICardapioItemRepository>();
        public IImpressaoPendenteRepository Impressoes { get; } = Substitute.For<IImpressaoPendenteRepository>();
        public IOperacaoEventPublisher Eventos { get; } = Substitute.For<IOperacaoEventPublisher>();
        public IUnitOfWork Uow { get; } = Substitute.For<IUnitOfWork>();
        public ICurrentUserAccessor Usuario { get; } = Substitute.For<ICurrentUserAccessor>();

        public Fakes()
        {
            Usuario.EmpresaId.Returns(EmpresaId);
            Usuario.Nivel.Returns(NivelAcesso.Operador);
        }

        public void Registrar(IServiceCollection s)
        {
            s.AddSingleton(Pedidos);
            s.AddSingleton(Clientes);
            s.AddSingleton(Storefronts);
            s.AddSingleton(Cardapio);
            s.AddSingleton(Impressoes);
            s.AddSingleton(Eventos);
            s.AddSingleton(Uow);
            s.AddSingleton<TimeProvider>(new RelogioFixo(Agora));
            s.AddScoped<MontarCanhotoUseCase>();
            s.AddScoped<ListarImpressoesPendentesUseCase>();
            s.AddScoped<RegistrarRetornoImpressaoUseCase>();
            s.AddScoped<ReimprimirCanhotoUseCase>();
        }

        public ImpressaoController Controller() => new(
            new MontarCanhotoUseCase(Pedidos, Clientes, Storefronts, Cardapio),
            new ListarImpressoesPendentesUseCase(Impressoes),
            new RegistrarRetornoImpressaoUseCase(Impressoes, Uow, new RelogioFixo(Agora), NullLogger<RegistrarRetornoImpressaoUseCase>.Instance),
            new ReimprimirCanhotoUseCase(Pedidos, Impressoes, Eventos, Uow, new RelogioFixo(Agora)),
            Usuario)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
    }

    [Fact]
    public async Task TextoCabeEm42Colunas()
    {
        var f = new Fakes();
        var storefront = StorefrontEntity.Criar(f.EmpresaId, "casa-da-baba", "Casa da Babá", 0m);
        var coxinha = CardapioItem.CriarAvulso(storefront.Id, "Coxinha", 10m);
        coxinha.AtualizarMetadata(sugestaoMolho: "Molho rosé da casa com páprica defumada");
        var pedido = Pedido.Criar(f.EmpresaId, origem: "whatsapp");
        pedido.ClienteNome = "Maria da Conceição Albuquerque de Assunção";
        pedido.ClienteTelefone = "+5511999990000";
        pedido.Observacoes = "Portão azul, tocar o interfone do bloco B — não bater palma";
        pedido.Itens.Add(new PedidoItem
        {
            Id = Guid.NewGuid(), PedidoId = pedido.Id, Nome = "Coxinha de frango com catupiry cremosíssima",
            Quantidade = 2, LinhaSnapshot = "paraServir", CardapioItemId = coxinha.Id,
            VariacaoRotuloSnapshot = "Cento", Observacao = "sem cebola, bem sequinha, embalar separado das outras", CriadoEm = Agora,
        });
        pedido.Itens.Add(new PedidoItem
        {
            Id = Guid.NewGuid(), PedidoId = pedido.Id, Nome = "Lasanha à bolonhesa", Quantidade = 1.5m,
            LinhaSnapshot = "prepararEmCasa", VariacaoRotuloSnapshot = "800g", CriadoEm = Agora.AddSeconds(1),
        });
        f.Pedidos.GetByIdWithDetailsAsync(f.EmpresaId, pedido.Id).Returns(pedido);
        f.Storefronts.GetByEmpresaAsync(f.EmpresaId, Arg.Any<CancellationToken>()).Returns(storefront);
        f.Cardapio.GetTodosDoStorefrontAsync(storefront.Id, Arg.Any<CancellationToken>()).Returns(new List<CardapioItem> { coxinha });

        var r = await f.Controller().Canhoto(pedido.Id, "texto", null, CancellationToken.None);

        var content = r.Should().BeOfType<ContentResult>().Subject;
        content.ContentType.Should().StartWith("text/plain");
        var linhas = content.Content!.Split('\n');
        linhas.Should().OnlyContain(l => l.Length <= 42, "a bobina de 80 mm tem 42 colunas");
        content.Content.Should().MatchRegex(@"^[\x20-\x7E\n]*$", "ESC/POS sem acentos nem caracteres fora do ASCII");
        var texto = string.Join(" ", linhas.Select(l => l.Trim()));
        texto.Should().Contain("PARA SERVIR").And.Contain("PREPARAR EM CASA");
        texto.Should().Contain("2x Coxinha de frango com catupiry cremosissima (Cento)");
        texto.Should().Contain("molho: Molho rose da casa com paprica defumada");
        texto.Should().Contain("obs: sem cebola, bem sequinha, embalar separado das outras");
        texto.Should().Contain("1,5x Lasanha a bolonhesa (800g)");
        texto.Should().Contain("Portao azul");
        texto.Should().Contain("imprima este canhoto: ele basta para produzir");
    }

    [Fact]
    public async Task HtmlEm80mmComCssEmbutidoEEscapado()
    {
        var f = new Fakes();
        var pedido = Pedido.Criar(f.EmpresaId, origem: "whatsapp");
        pedido.ClienteNome = "<script>alert(1)</script>";
        pedido.Itens.Add(new PedidoItem
        {
            Id = Guid.NewGuid(), PedidoId = pedido.Id, Nome = "Coxinha", Quantidade = 1,
            LinhaSnapshot = "paraServir", CriadoEm = Agora,
        });
        f.Pedidos.GetByIdWithDetailsAsync(f.EmpresaId, pedido.Id).Returns(pedido);

        var r = await f.Controller().Canhoto(pedido.Id, null, null, CancellationToken.None);

        var content = r.Should().BeOfType<ContentResult>().Subject;
        content.ContentType.Should().StartWith("text/html");
        content.Content.Should().Contain("80mm").And.Contain("@media print").And.Contain("Coxinha");
        content.Content.Should().NotContain("<script>alert(1)</script>");
    }

    [Fact]
    public async Task ImpressaIdempotente()
    {
        var f = new Fakes();
        var impressao = ImpressaoPendente.CriarCanhoto(f.EmpresaId, null, Guid.NewGuid(), Agora.AddMinutes(-1));
        f.Impressoes.GetByIdAsync(f.EmpresaId, impressao.Id, Arg.Any<CancellationToken>()).Returns(impressao);
        var controller = f.Controller();

        var primeira = await controller.Impressa(impressao.Id, null, CancellationToken.None);
        var segunda = await controller.Impressa(impressao.Id, null, CancellationToken.None);

        primeira.Should().BeOfType<OkObjectResult>();
        segunda.Should().BeOfType<OkObjectResult>();
        impressao.Status.Should().Be(StatusImpressao.Impressa);
        impressao.ImpressaEm.Should().Be(Agora);
        impressao.Tentativas.Should().Be(1);
        await f.Uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task ImpressaDeOutraEmpresa404()
    {
        var f = new Fakes();

        var r = await f.Controller().Impressa(Guid.NewGuid(), null, CancellationToken.None);

        r.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task FormatoInvalido400()
    {
        var f = new Fakes();

        var r = await f.Controller().Canhoto(Guid.NewGuid(), "pdf", null, CancellationToken.None);

        r.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task PendentesComApiKeyDoBridgeSoDaEmpresaConfiguradaSemCredencial401()
    {
        var f = new Fakes();
        var outraEmpresa = Guid.NewGuid();
        var daEmpresa = ImpressaoPendente.CriarCanhoto(f.EmpresaId, null, Guid.NewGuid(), Agora);
        var daOutra = ImpressaoPendente.CriarCanhoto(outraEmpresa, null, Guid.NewGuid(), Agora);
        f.Impressoes.ListarPendentesAsync(f.EmpresaId, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(new[] { daEmpresa });
        f.Impressoes.ListarPendentesAsync(outraEmpresa, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(new[] { daOutra });

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Impressao:ApiKey"] = "chave-do-bridge-de-teste",
            ["Impressao:EmpresaId"] = f.EmpresaId.ToString(),
        });
        f.Registrar(builder.Services);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentUserAccessor, EasyStock.Api.Services.CurrentUserAccessor>();
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o => o.TokenValidationParameters = new TokenValidationParameters
            {
                ValidIssuer = "teste",
                ValidAudience = "teste",
                IssuerSigningKey = new SymmetricSecurityKey(new byte[32]),
            })
            .AddImpressaoApiKeyScheme(builder.Configuration);
        builder.Services.AddAuthorization(o =>
        {
            o.AddPolicy("Operador", p => p.RequireClaim("nivel", "SuperAdmin", "Admin", "Gerente", "Operador"));
            o.AddImpressaoFilaPolicy();
        });
        builder.Services.AddControllers()
            .ConfigureApplicationPartManager(m =>
            {
                m.ApplicationParts.Add(new Microsoft.AspNetCore.Mvc.ApplicationParts.AssemblyPart(typeof(ImpressaoController).Assembly));
                m.FeatureProviders.Clear();
                m.FeatureProviders.Add(new SoImpressao());
            });

        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync();
        var endereco = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        using var http = new HttpClient { BaseAddress = new Uri(endereco) };

        var semCredencial = await http.GetAsync("api/impressao/pendentes");
        var chaveErrada = await http.SendAsync(ComChave("api/impressao/pendentes", "outra-chave"));
        var canhotoSemCredencial = await http.GetAsync($"api/pedidos/{Guid.NewGuid()}/canhoto?formato=texto");
        var reimprimirComChave = await http.SendAsync(ComChave($"api/pedidos/{Guid.NewGuid()}/reimprimir", "chave-do-bridge-de-teste", HttpMethod.Post));
        var comChave = await http.SendAsync(ComChave("api/impressao/pendentes", "chave-do-bridge-de-teste"));

        semCredencial.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        chaveErrada.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        canhotoSemCredencial.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        reimprimirComChave.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "reimprimir é só do operador (JWT)");
        comChave.StatusCode.Should().Be(HttpStatusCode.OK);
        var corpo = await comChave.Content.ReadFromJsonAsync<JsonElement>();
        var ids = corpo.GetProperty("data").EnumerateArray().Select(e => e.GetProperty("id").GetGuid()).ToList();
        ids.Should().Equal(daEmpresa.Id);
        await f.Impressoes.DidNotReceive().ListarPendentesAsync(outraEmpresa, Arg.Any<int>(), Arg.Any<CancellationToken>());
        await app.StopAsync();
    }

    private static HttpRequestMessage ComChave(string url, string chave, HttpMethod? metodo = null)
    {
        var req = new HttpRequestMessage(metodo ?? HttpMethod.Get, url);
        req.Headers.Add(ImpressaoApiKeyAuthHandler.HeaderName, chave);
        return req;
    }

    private sealed class SoImpressao : ControllerFeatureProvider
    {
        protected override bool IsController(TypeInfo typeInfo) => typeInfo.AsType() == typeof(ImpressaoController);
    }
}
