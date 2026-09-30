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
            Conversa = Conversa.Abrir(Storefront.EmpresaId, "5511999998888", Agora, "Maria", Guid.NewGuid());

            var saudacao = new SaudacaoAtendimento(StorefrontRepo, new ConfigurationBuilder().Build());
            Ferramenta = new EnviarCardapioImagemFerramenta(StorefrontRepo, new LinkCardapioConversaService(Links, saudacao),
                Cloud, Substitute.For<IConversaRepository>());
        }

        public async Task<JsonElement> ExecutarAsync()
        {
            var resultado = await Ferramenta.ExecutarAsync(
                new ContextoTurnoAgente(Storefront.EmpresaId, Conversa, Agora), JsonSerializer.SerializeToElement(new { }));
            return JsonDocument.Parse(resultado).RootElement.Clone();
        }
    }

    [Fact]
    public async Task LegendaLevaOLinkComTokenDaConversa()
    {
        var c = new Cenario("https://cdn.test/cardapio.png");

        var json = await c.ExecutarAsync();

        var link = json.GetProperty("link").GetString()!;
        link.Should().StartWith($"{SaudacaoAtendimento.BaseUrlPadrao}{SaudacaoAtendimento.CaminhoCardapio}?c=");
        await c.Cloud.Received(1).EnviarImagemAsync(c.Conversa.ContatoIdExterno, "https://cdn.test/cardapio.png", link,
            Arg.Any<CancellationToken>());
        c.Links.Links.Should().ContainSingle().Which.ConversaId.Should().Be(c.Conversa.Id);
    }

    [Fact]
    public async Task SemImagem_DevolveOLinkComTokenParaOAgente()
    {
        var c = new Cenario(null);

        var json = await c.ExecutarAsync();

        json.GetProperty("enviado").GetBoolean().Should().BeFalse();
        json.GetProperty("link").GetString().Should().Contain("?c=");
        c.Links.Links.Should().ContainSingle();
    }
}
