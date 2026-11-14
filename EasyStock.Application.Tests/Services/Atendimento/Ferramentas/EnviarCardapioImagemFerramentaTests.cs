using System.Text.Json;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Services.Atendimento.Ferramentas;
using EasyStock.Domain.Entities.Atendimento;
using Microsoft.Extensions.Configuration;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Application.Tests.Services.Atendimento.Ferramentas;

/// <summary>S48: o cardápio enviado na conversa leva o link com token que amarra o carrinho à conversa.</summary>
public class EnviarCardapioImagemFerramentaTests
{
    private static readonly DateTime Agora = new(2026, 9, 30, 15, 0, 0, DateTimeKind.Utc);

    private sealed class Cenario
    {
        public StorefrontEntity Storefront { get; } = StorefrontEntity.Criar(Guid.NewGuid(), "casa-da-baba", "Casa da Babá", 0m);
        public IStorefrontRepository StorefrontRepo { get; } = Substitute.For<IStorefrontRepository>();
        public IWhatsAppCloudClient Cloud { get; } = Substitute.For<IWhatsAppCloudClient>();
        public LinkCardapioConversaServiceTests.RepositorioEmMemoria Links { get; } = new();
        public Conversa Conversa { get; }
        public EnviarCardapioImagemFerramenta Ferramenta { get; }

        public Cenario(string? imagemUrl)
        {
            if (imagemUrl is not null) typeof(StorefrontEntity).GetProperty("CardapioImagemUrl")!.SetValue(Storefront, imagemUrl);
            StorefrontRepo.GetByEmpresaAsync(Storefront.EmpresaId, Arg.Any<CancellationToken>()).Returns(Storefront);
            Cloud.EnviarImagemAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
                .Returns(new EnvioWhatsAppResult("wamid.1"));
            Cloud.EnviarTextoAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
                .Returns(new EnvioWhatsAppResult("wamid.texto"));
            Conversa = Conversa.Abrir(Storefront.EmpresaId, "5511999998888", Agora, "Maria", Guid.NewGuid());

            var saudacao = new SaudacaoAtendimento(StorefrontRepo, new ConfigurationBuilder().Build());
            Ferramenta = new EnviarCardapioImagemFerramenta(StorefrontRepo, new LinkCardapioConversaService(Links, saudacao),
                Cloud, Substitute.For<IConversaRepository>());
        }

        public ContextoTurnoAgente NovoContexto() => new(Storefront.EmpresaId, Conversa, Agora);

        public async Task<JsonElement> ExecutarAsync(object? entrada = null, ContextoTurnoAgente? contexto = null)
        {
            var resultado = await Ferramenta.ExecutarAsync(
                contexto ?? NovoContexto(), JsonSerializer.SerializeToElement(entrada ?? new { }));
            return JsonDocument.Parse(resultado).RootElement.Clone();
        }
    }

    [Fact]
    public async Task LegendaLevaOLinkComTokenDaConversa()
    {
        var c = new Cenario("https://cdn.test/cardapio.png");

        var json = await c.ExecutarAsync();

        var link = json.GetProperty("link").GetString()!;
        link.Should().StartWith($"{SaudacaoAtendimento.BaseUrlPadrao}/comanda.html?c=");
        await c.Cloud.Received(1).EnviarImagemAsync(c.Conversa.ContatoIdExterno, "https://cdn.test/cardapio.png",
            Arg.Is<string>(texto => texto.Contains(link) && texto.Contains("comanda pessoal")),
            Arg.Any<CancellationToken>());
        c.Links.Links.Should().ContainSingle().Which.ConversaId.Should().Be(c.Conversa.Id);
    }

    [Fact]
    public async Task SemImagem_EnviaComandaEmTextoERegistraMensagem()
    {
        var c = new Cenario(null);

        var json = await c.ExecutarAsync();

        json.GetProperty("enviado").GetBoolean().Should().BeTrue();
        json.GetProperty("link").GetString().Should().Contain("?c=");
        await c.Cloud.Received(1).EnviarTextoAsync(c.Conversa.ContatoIdExterno,
            Arg.Is<string>(texto => texto.Contains("comanda pessoal") && texto.Contains("/comanda.html?c=")),
            null, Arg.Any<CancellationToken>());
        await c.Cloud.DidNotReceiveWithAnyArgs().EnviarImagemAsync(default!, default!, default, default);
        c.Links.Links.Should().ContainSingle();
    }

    /// <summary>
    /// #2516: o cliente disse "classica tamanho 600g" e o agente reenviou o link, que expira o anterior e descarta o texto
    /// do turno. Com a comanda vigente, a ferramenta não reenvia sem pedido do cliente e o texto do agente sai.
    /// </summary>
    [Fact]
    public async Task ComandaVigente_SemReenviar_NaoEnviaDeNovo()
    {
        var c = new Cenario(null);
        await c.ExecutarAsync();
        c.Cloud.ClearReceivedCalls();
        var contexto = c.NovoContexto();

        var json = await c.ExecutarAsync(contexto: contexto);

        json.GetProperty("enviado").GetBoolean().Should().BeFalse();
        json.GetProperty("motivo").GetString().Should().Be("comanda_ja_enviada");
        contexto.EnvioVerificado.Should().BeFalse();
        await c.Cloud.DidNotReceiveWithAnyArgs().EnviarTextoAsync(default!, default!, default, default);
        await c.Cloud.DidNotReceiveWithAnyArgs().EnviarImagemAsync(default!, default!, default, default);
        c.Links.Links.Should().ContainSingle().Which.EstaValido(Agora).Should().BeTrue();
    }

    [Fact]
    public async Task ClientePedeOLinkDeNovo_ReenviaNovaEdicao()
    {
        var c = new Cenario(null);
        await c.ExecutarAsync();

        var json = await c.ExecutarAsync(new { reenviar = true });

        json.GetProperty("enviado").GetBoolean().Should().BeTrue();
        await c.Cloud.Received(2).EnviarTextoAsync(c.Conversa.ContatoIdExterno, Arg.Any<string>(), null, Arg.Any<CancellationToken>());
        c.Links.Links.Should().HaveCount(2);
    }

    [Fact]
    public async Task ComandaJaUsada_EnviaNovaSemPrecisarDeReenviar()
    {
        var c = new Cenario(null);
        await c.ExecutarAsync();
        c.Links.Links.Single().Consumir(Agora);

        var json = await c.ExecutarAsync();

        json.GetProperty("enviado").GetBoolean().Should().BeTrue();
        c.Links.Links.Should().HaveCount(2);
    }
}
