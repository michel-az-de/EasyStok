using EasyStock.Domain.Entities.Banners;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Exceptions;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities;

public class BannerTests
{
    // ── Helpers de conteúdo válido ──────────────────────────────────────────
    private static BannerConteudo ImagemValida(Action<BannerConteudoBuilder>? ajustar = null)
    {
        var b = new BannerConteudoBuilder
        {
            TituloInterno = "Promoção de Julho",
            Tipo = BannerTipo.Imagem,
            ImagemStorageKey = "banners/abc.png",
            ImagemUrl = "https://cdn.easystok.com/banners/abc.png"
        };
        ajustar?.Invoke(b);
        return b.Build();
    }

    private static BannerConteudo MensagemValida(Action<BannerConteudoBuilder>? ajustar = null)
    {
        var b = new BannerConteudoBuilder
        {
            TituloInterno = "Aviso de manutenção",
            Tipo = BannerTipo.Mensagem,
            Corpo = "O sistema ficará indisponível domingo às 2h."
        };
        ajustar?.Invoke(b);
        return b.Build();
    }

    // ── Criar: caminhos felizes ─────────────────────────────────────────────
    [Fact]
    public void Criar_com_imagem_valida_gera_banner_inativo_por_default()
    {
        var banner = Banner.Criar(ImagemValida(), criadoPorUsuarioId: Guid.NewGuid());

        banner.Id.Should().NotBeEmpty();
        banner.Tipo.Should().Be(BannerTipo.Imagem);
        banner.ImagemStorageKey.Should().Be("banners/abc.png");
        banner.Ativo.Should().BeFalse();
        banner.CriadoEm.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
        banner.AtualizadoEm.Should().Be(banner.CriadoEm);
    }

    [Fact]
    public void Criar_com_mensagem_valida_persiste_corpo()
    {
        var banner = Banner.Criar(MensagemValida());

        banner.Tipo.Should().Be(BannerTipo.Mensagem);
        banner.Corpo.Should().Be("O sistema ficará indisponível domingo às 2h.");
        banner.ImagemStorageKey.Should().BeNull();
    }

    [Fact]
    public void Criar_faz_trim_do_titulo()
    {
        var banner = Banner.Criar(ImagemValida(b => b.TituloInterno = "  Título  "));
        banner.TituloInterno.Should().Be("Título");
    }

    // ── Invariante: imagem × texto ──────────────────────────────────────────
    [Fact]
    public void Banner_imagem_sem_storage_key_lanca()
    {
        var act = () => Banner.Criar(ImagemValida(b => b.ImagemStorageKey = null));
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Banner_mensagem_sem_corpo_lanca(string? corpo)
    {
        var act = () => Banner.Criar(MensagemValida(b => b.Corpo = corpo));
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void Corpo_excede_4000_lanca()
    {
        var act = () => Banner.Criar(MensagemValida(b => b.Corpo = new string('a', 4001)));
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Titulo_vazio_lanca(string? titulo)
    {
        var act = () => Banner.Criar(ImagemValida(b => b.TituloInterno = titulo!));
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void Titulo_excede_120_lanca()
    {
        var act = () => Banner.Criar(ImagemValida(b => b.TituloInterno = new string('a', 121)));
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    // ── Invariante: link/URL ────────────────────────────────────────────────
    [Fact]
    public void Link_habilitado_com_url_valida_persiste()
    {
        var banner = Banner.Criar(ImagemValida(b =>
        {
            b.LinkAtivo = true;
            b.LinkUrl = "https://easystok.com/promo";
            b.NovaAba = true;
        }));

        banner.LinkAtivo.Should().BeTrue();
        banner.LinkUrl.Should().Be("https://easystok.com/promo");
        banner.NovaAba.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html;base64,PHNjcmlwdD4=")]
    [InlineData("ftp://x/y")]
    [InlineData("/relativo")]
    [InlineData("easystok.com/sem-esquema")]
    public void Link_habilitado_com_url_nao_http_lanca(string? url)
    {
        var act = () => Banner.Criar(ImagemValida(b =>
        {
            b.LinkAtivo = true;
            b.LinkUrl = url;
        }));
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void Link_em_banner_de_mensagem_lanca()
    {
        var act = () => Banner.Criar(MensagemValida(b =>
        {
            b.LinkAtivo = true;
            b.LinkUrl = "https://easystok.com";
        }));
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void Link_desabilitado_limpa_url_e_nova_aba()
    {
        var banner = Banner.Criar(ImagemValida(b =>
        {
            b.LinkAtivo = false;
            b.LinkUrl = "https://easystok.com";
            b.NovaAba = true;
        }));

        banner.LinkUrl.Should().BeNull();
        banner.NovaAba.Should().BeFalse();
    }

    // ── Invariante: tooltip ─────────────────────────────────────────────────
    [Fact]
    public void Tooltip_habilitado_exige_texto()
    {
        var act = () => Banner.Criar(ImagemValida(b =>
        {
            b.TooltipAtivo = true;
            b.TooltipTexto = "   ";
        }));
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void Tooltip_em_banner_de_mensagem_lanca()
    {
        var act = () => Banner.Criar(MensagemValida(b =>
        {
            b.TooltipAtivo = true;
            b.TooltipTexto = "dica";
        }));
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    // ── Invariante: tamanho manual ──────────────────────────────────────────
    [Theory]
    [InlineData(null, 100)]
    [InlineData(100, null)]
    [InlineData(0, 100)]
    [InlineData(100, 0)]
    [InlineData(-5, 100)]
    public void Tamanho_manual_exige_largura_e_altura_positivas(int? largura, int? altura)
    {
        var act = () => Banner.Criar(ImagemValida(b =>
        {
            b.TamanhoModo = BannerTamanhoModo.Manual;
            b.LarguraPx = largura;
            b.AlturaPx = altura;
        }));
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void Tamanho_manual_com_dimensoes_positivas_persiste()
    {
        var banner = Banner.Criar(ImagemValida(b =>
        {
            b.TamanhoModo = BannerTamanhoModo.Manual;
            b.LarguraPx = 320;
            b.AlturaPx = 100;
        }));

        banner.LarguraPx.Should().Be(320);
        banner.AlturaPx.Should().Be(100);
    }

    [Fact]
    public void Tamanho_herdado_ignora_dimensoes()
    {
        var banner = Banner.Criar(ImagemValida(b =>
        {
            b.TamanhoModo = BannerTamanhoModo.HerdadoDaImagem;
            b.LarguraPx = 320;
            b.AlturaPx = 100;
        }));

        banner.LarguraPx.Should().BeNull();
        banner.AlturaPx.Should().BeNull();
    }

    // ── Invariante: janela ──────────────────────────────────────────────────
    [Fact]
    public void Janela_fim_antes_ou_igual_ao_inicio_lanca()
    {
        var inicio = new DateTime(2026, 7, 10, 12, 0, 0, DateTimeKind.Utc);
        var act = () => Banner.Criar(ImagemValida(b =>
        {
            b.InicioEm = inicio;
            b.FimEm = inicio.AddHours(-1);
        }));
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void Janela_valida_persiste_em_utc()
    {
        var inicio = new DateTime(2026, 7, 10, 12, 0, 0, DateTimeKind.Utc);
        var banner = Banner.Criar(ImagemValida(b =>
        {
            b.InicioEm = inicio;
            b.FimEm = inicio.AddDays(7);
        }));

        banner.InicioEm!.Value.Kind.Should().Be(DateTimeKind.Utc);
        banner.FimEm!.Value.Kind.Should().Be(DateTimeKind.Utc);
        banner.FimEm.Should().BeAfter(banner.InicioEm!.Value);
    }

    // ── Ciclo de vida ───────────────────────────────────────────────────────
    [Fact]
    public void Ativar_e_idempotente()
    {
        var banner = Banner.Criar(ImagemValida());
        banner.Ativar();
        var alterado = banner.AtualizadoEm;

        Thread.Sleep(5);
        banner.Ativar();

        banner.Ativo.Should().BeTrue();
        banner.AtualizadoEm.Should().Be(alterado);
    }

    [Fact]
    public void Desativar_e_idempotente()
    {
        var banner = Banner.Criar(ImagemValida(b => b.Ativo = true));
        banner.Ativar();
        banner.Desativar();
        var alterado = banner.AtualizadoEm;

        Thread.Sleep(5);
        banner.Desativar();

        banner.Ativo.Should().BeFalse();
        banner.AtualizadoEm.Should().Be(alterado);
    }

    [Fact]
    public void RegistrarNotificacao_carimba_uma_vez_e_e_idempotente()
    {
        var banner = Banner.Criar(ImagemValida());
        var primeira = new DateTime(2026, 7, 10, 12, 0, 0, DateTimeKind.Utc);

        banner.RegistrarNotificacao(primeira);
        banner.RegistrarNotificacao(primeira.AddHours(1));

        banner.NotificadoEm.Should().Be(primeira);
    }

    // ── Atualizar revalida (PUT não é porta dos fundos) ─────────────────────
    [Fact]
    public void Atualizar_com_dados_validos_altera_e_carimba_atualizadoEm()
    {
        var banner = Banner.Criar(MensagemValida());
        Thread.Sleep(5);
        var antes = banner.AtualizadoEm;

        banner.Atualizar(MensagemValida(b => b.Corpo = "Novo corpo"));

        banner.Corpo.Should().Be("Novo corpo");
        banner.AtualizadoEm.Should().BeAfter(antes);
    }

    [Fact]
    public void Atualizar_com_url_javascript_lanca()
    {
        var banner = Banner.Criar(ImagemValida());
        var act = () => banner.Atualizar(ImagemValida(b =>
        {
            b.LinkAtivo = true;
            b.LinkUrl = "javascript:alert(1)";
        }));
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void Atualizar_com_tamanho_manual_invalido_lanca()
    {
        var banner = Banner.Criar(ImagemValida());
        var act = () => banner.Atualizar(ImagemValida(b =>
        {
            b.TamanhoModo = BannerTamanhoModo.Manual;
            b.LarguraPx = 0;
            b.AlturaPx = 100;
        }));
        act.Should().Throw<RegraDeDominioVioladaException>();
    }
}

public class BannerConfirmacaoTests
{
    [Fact]
    public void Criar_gera_confirmacao_com_timestamp()
    {
        var c = BannerConfirmacao.Criar(Guid.NewGuid(), Guid.NewGuid(), BannerInteracaoTipo.Confirmado);

        c.Id.Should().NotBeEmpty();
        c.Tipo.Should().Be(BannerInteracaoTipo.Confirmado);
        c.RegistradoEm.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Criar_exige_bannerId()
    {
        var act = () => BannerConfirmacao.Criar(Guid.Empty, Guid.NewGuid(), BannerInteracaoTipo.Visto);
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void Criar_exige_usuarioId()
    {
        var act = () => BannerConfirmacao.Criar(Guid.NewGuid(), Guid.Empty, BannerInteracaoTipo.Visto);
        act.Should().Throw<RegraDeDominioVioladaException>();
    }
}

/// <summary>
/// Builder mutável só para os testes montarem <see cref="BannerConteudo"/> (record com
/// <c>init</c>) de forma incremental via <c>Action</c>.
/// </summary>
public sealed class BannerConteudoBuilder
{
    public string TituloInterno { get; set; } = "Banner";
    public BannerTipo Tipo { get; set; } = BannerTipo.Imagem;
    public string? Corpo { get; set; }
    public string? ImagemStorageKey { get; set; }
    public string? ImagemUrl { get; set; }
    public bool LinkAtivo { get; set; }
    public string? LinkUrl { get; set; }
    public bool NovaAba { get; set; }
    public bool TooltipAtivo { get; set; }
    public string? TooltipTexto { get; set; }
    public BannerTamanhoModo TamanhoModo { get; set; } = BannerTamanhoModo.HerdadoDaImagem;
    public int? LarguraPx { get; set; }
    public int? AlturaPx { get; set; }
    public bool VisualizacaoUnica { get; set; }
    public bool ExigeConfirmacao { get; set; }
    public bool NotificarAoPublicar { get; set; }
    public bool Ativo { get; set; }
    public DateTime? InicioEm { get; set; }
    public DateTime? FimEm { get; set; }
    public int Prioridade { get; set; }

    public BannerConteudo Build() => new()
    {
        TituloInterno = TituloInterno,
        Tipo = Tipo,
        Corpo = Corpo,
        ImagemStorageKey = ImagemStorageKey,
        ImagemUrl = ImagemUrl,
        LinkAtivo = LinkAtivo,
        LinkUrl = LinkUrl,
        NovaAba = NovaAba,
        TooltipAtivo = TooltipAtivo,
        TooltipTexto = TooltipTexto,
        TamanhoModo = TamanhoModo,
        LarguraPx = LarguraPx,
        AlturaPx = AlturaPx,
        VisualizacaoUnica = VisualizacaoUnica,
        ExigeConfirmacao = ExigeConfirmacao,
        NotificarAoPublicar = NotificarAoPublicar,
        Ativo = Ativo,
        InicioEm = InicioEm,
        FimEm = FimEm,
        Prioridade = Prioridade
    };
}
