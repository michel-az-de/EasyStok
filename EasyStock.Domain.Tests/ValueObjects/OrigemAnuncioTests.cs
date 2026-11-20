using EasyStock.Domain.ValueObjects;
using FluentAssertions;

namespace EasyStock.Domain.Tests.ValueObjects;

public class OrigemAnuncioTests
{
    private static readonly DateTimeOffset Agora = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
    private static readonly OrigemAnuncio.IdsDeClique SemIds = new(null, null, null, null);
    private static OrigemAnuncio.Utms U(string? fonte = null, string? campanha = null, string? meio = null,
        string? conteudo = null, string? termo = null) => new(fonte, meio, campanha, conteudo, termo);

    [Fact]
    public void DoSite_normaliza_texto_trim_vazio_vira_null_e_remove_controle()
    {
        var o = OrigemAnuncio.DoSite(U("  google ", "pascoa\u0000\n2026", "   ", "", null), SemIds,
            OrigemAnuncio.ViaSite, true, Agora);

        o!.Fonte.Should().Be("google");
        o.Campanha.Should().Be("pascoa2026");
        o.Meio.Should().BeNull();
        o.Conteudo.Should().BeNull();
        o.Termo.Should().BeNull();
        o.Via.Should().Be("site");
        o.CapturadaEm.Should().Be(Agora);
    }

    [Fact]
    public void DoSite_corta_no_tamanho_maximo()
    {
        var longo = new string('a', 300);
        var o = OrigemAnuncio.DoSite(U("google", longo, longo, longo, longo),
            new("x" + new string('9', 400), null, null, null), OrigemAnuncio.ViaSite, true, Agora)!;

        o.Campanha!.Length.Should().Be(OrigemAnuncio.TamanhoTexto).And.Be(120);
        o.Meio!.Length.Should().Be(120);
        o.Conteudo!.Length.Should().Be(120);
        o.Termo!.Length.Should().Be(120);
        o.IdClique!.Length.Should().Be(OrigemAnuncio.TamanhoIdClique).And.Be(200);
    }

    [Fact]
    public void DoSite_escolhe_um_id_por_prioridade_gclid_gbraid_wbraid_fbclid()
    {
        OrigemAnuncio.DoSite(U(), new("g", "gb", "wb", "fb"), OrigemAnuncio.ViaSite, true, Agora)!
            .Should().Match<OrigemAnuncio>(o => o.TipoIdClique == "gclid" && o.IdClique == "g");
        OrigemAnuncio.DoSite(U(), new(null, "gb", "wb", "fb"), OrigemAnuncio.ViaSite, true, Agora)!
            .Should().Match<OrigemAnuncio>(o => o.TipoIdClique == "gbraid" && o.IdClique == "gb");
        OrigemAnuncio.DoSite(U(), new(null, null, "wb", "fb"), OrigemAnuncio.ViaSite, true, Agora)!
            .Should().Match<OrigemAnuncio>(o => o.TipoIdClique == "wbraid" && o.IdClique == "wb");
        OrigemAnuncio.DoSite(U(), new(null, null, null, "fb"), OrigemAnuncio.ViaSite, true, Agora)!
            .Should().Match<OrigemAnuncio>(o => o.TipoIdClique == "fbclid" && o.IdClique == "fb");
    }

    [Fact]
    public void DoSite_sem_consentimento_descarta_id_mas_mantem_utm_e_plataforma_da_fonte()
    {
        var o = OrigemAnuncio.DoSite(U("google", "c1"), new("g", null, null, null),
            OrigemAnuncio.ViaSite, false, Agora)!;

        o.IdClique.Should().BeNull();
        o.TipoIdClique.Should().BeNull();
        o.Campanha.Should().Be("c1");
        o.Plataforma.Should().Be("google");
    }

    [Fact]
    public void DoSite_sem_consentimento_e_sem_utm_retorna_null()
    {
        OrigemAnuncio.DoSite(U(), new("g", null, null, null), OrigemAnuncio.ViaSite, false, Agora)
            .Should().BeNull();
    }

    [Theory]
    [InlineData("gclid", "google")]
    [InlineData("gbraid", "google")]
    [InlineData("wbraid", "google")]
    [InlineData("fbclid", "meta")]
    public void DoSite_deriva_plataforma_do_id_de_clique(string tipo, string esperada)
    {
        var ids = tipo switch
        {
            "gclid" => new OrigemAnuncio.IdsDeClique("v", null, null, null),
            "gbraid" => new(null, "v", null, null),
            "wbraid" => new(null, null, "v", null),
            _ => new OrigemAnuncio.IdsDeClique(null, null, null, "v"),
        };
        OrigemAnuncio.DoSite(U("newsletter"), ids, OrigemAnuncio.ViaSite, true, Agora)!
            .Plataforma.Should().Be(esperada);
    }

    [Theory]
    [InlineData("Google", "google")]
    [InlineData("google_ads", "google")]
    [InlineData("facebook", "meta")]
    [InlineData("Instagram", "meta")]
    [InlineData("meta", "meta")]
    [InlineData("meta_ads", "meta")]
    [InlineData("Meta-Ads", "meta")]
    [InlineData("metabase", "outra")]
    [InlineData("metas_novembro", "outra")]
    [InlineData("fb", "meta")]
    [InlineData("ig", "meta")]
    [InlineData("newsletter", "outra")]
    public void DoSite_deriva_plataforma_do_utm_source(string fonte, string esperada)
    {
        OrigemAnuncio.DoSite(U(fonte), SemIds, OrigemAnuncio.ViaSite, true, Agora)!
            .Plataforma.Should().Be(esperada);
    }

    [Fact]
    public void DoSite_sem_utm_e_sem_id_retorna_null()
    {
        OrigemAnuncio.DoSite(U(null, "  "), SemIds, OrigemAnuncio.ViaSite, true, Agora).Should().BeNull();
    }

    [Fact]
    public void DoAnuncioWhatsApp_preenche_meta_e_ctwa()
    {
        var o = OrigemAnuncio.DoAnuncioWhatsApp(" 123 ", "  Promo  ", "ctwa-abc", Agora);

        o.Plataforma.Should().Be("meta");
        o.Via.Should().Be("anuncio_whatsapp");
        o.IdAnuncio.Should().Be("123");
        o.TituloAnuncio.Should().Be("Promo");
        o.TipoIdClique.Should().Be("ctwa_clid");
        o.IdClique.Should().Be("ctwa-abc");
        o.CapturadaEm.Should().Be(Agora);
    }

    [Fact]
    public void DoAnuncioWhatsApp_sem_ctwa_nao_tem_tipo_de_id()
    {
        var o = OrigemAnuncio.DoAnuncioWhatsApp("1", new string('t', 500), null, Agora);

        o.TipoIdClique.Should().BeNull();
        o.IdClique.Should().BeNull();
        o.TituloAnuncio!.Length.Should().Be(OrigemAnuncio.TamanhoTexto);
    }

    [Fact]
    public void SemIdClique_devolve_copia_sem_id_e_tipo_preservando_o_resto()
    {
        var o = OrigemAnuncio.DoAnuncioWhatsApp("1", "t", "ctwa", Agora);

        var limpa = o.SemIdClique();

        limpa.IdClique.Should().BeNull();
        limpa.TipoIdClique.Should().BeNull();
        limpa.Plataforma.Should().Be("meta");
        limpa.IdAnuncio.Should().Be("1");
        o.IdClique.Should().Be("ctwa");
    }
}
