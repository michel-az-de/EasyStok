using EasyStock.Domain.ValueObjects;

namespace EasyStock.Application.Tests.UseCases.Common;

/// <summary>#2928 S4: o corpo <c>campanha</c> do site vira <see cref="OrigemAnuncio"/> num ponto só.</summary>
public class CampanhaInputTests
{
    private static readonly DateTimeOffset Agora = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ParaOrigem_ComMarketing_GuardaUtmEIdDeClique()
    {
        var campanha = new CampanhaInput(UtmSource: "google", UtmMedium: "cpc", UtmCampaign: "natal", Gclid: "abc", Marketing: true);

        var origem = campanha.ParaOrigem(OrigemAnuncio.ViaSite, Agora);

        origem.Should().NotBeNull();
        origem!.Plataforma.Should().Be(OrigemAnuncio.PlataformaGoogle);
        origem.Via.Should().Be(OrigemAnuncio.ViaSite);
        origem.Campanha.Should().Be("natal");
        origem.TipoIdClique.Should().Be(OrigemAnuncio.TipoGclid);
        origem.IdClique.Should().Be("abc");
        origem.CapturadaEm.Should().Be(Agora);
    }

    [Fact]
    public void ParaOrigem_SemMarketing_DescartaIdDeCliqueEMantemUtm()
    {
        var campanha = new CampanhaInput(UtmSource: "google", UtmCampaign: "natal", Gclid: "abc", Marketing: false);

        var origem = campanha.ParaOrigem(OrigemAnuncio.ViaSiteChat, Agora);

        origem.Should().NotBeNull();
        origem!.IdClique.Should().BeNull();
        origem.TipoIdClique.Should().BeNull();
        origem.Campanha.Should().Be("natal");
        origem.Via.Should().Be(OrigemAnuncio.ViaSiteChat);
    }

    [Fact]
    public void ParaOrigem_SoIdDeCliqueSemMarketing_ViraNulo()
    {
        new CampanhaInput(Gclid: "abc", Marketing: false).ParaOrigem(OrigemAnuncio.ViaSite, Agora).Should().BeNull();
    }

    [Fact]
    public void ParaOrigem_Vazia_ViraNulo()
    {
        new CampanhaInput().ParaOrigem(OrigemAnuncio.ViaSite, Agora).Should().BeNull();
        new CampanhaInput(UtmSource: "  ", Marketing: true).ParaOrigem(OrigemAnuncio.ViaSite, Agora).Should().BeNull();
    }

    [Fact]
    public void Resolver_UltimoAnuncioVence_ECaiNaDaConversaSemCampanha()
    {
        var daConversa = OrigemAnuncio.DoAnuncioWhatsApp("111", "Promo", null, Agora);
        var nova = new CampanhaInput(UtmSource: "instagram", UtmCampaign: "outra");

        CampanhaInput.Resolver(nova, daConversa, OrigemAnuncio.ViaSite, Agora)!.Campanha.Should().Be("outra");
        CampanhaInput.Resolver(null, daConversa, OrigemAnuncio.ViaSite, Agora).Should().BeSameAs(daConversa);
        CampanhaInput.Resolver(new CampanhaInput(), daConversa, OrigemAnuncio.ViaSite, Agora).Should().BeSameAs(daConversa);
        CampanhaInput.Resolver(null, null, OrigemAnuncio.ViaSite, Agora).Should().BeNull();
    }
}
