using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities;

public class FaqCategoriaTests
{
    [Fact]
    public void Criar_normaliza_slug_e_carimba_datas()
    {
        var c = FaqCategoria.Criar("Cadastros", "  CADASTROS  ", "descrição", "📦", 5);

        c.Id.Should().NotBeEmpty();
        c.Slug.Should().Be("cadastros");
        c.Nome.Should().Be("Cadastros");
        c.Publica.Should().BeTrue();
        c.CriadoEm.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
        c.AtualizadoEm.Should().Be(c.CriadoEm);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Criar_falha_se_nome_vazio(string nome)
    {
        var act = () => FaqCategoria.Criar(nome, "slug");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Atualizar_modifica_atualizadoEm()
    {
        var c = FaqCategoria.Criar("Cadastros", "cadastros");
        Thread.Sleep(5);
        var antes = c.AtualizadoEm;

        c.Atualizar("Cadastros e Configuracoes", "nova descricao", null, 1, false);

        c.Nome.Should().Be("Cadastros e Configuracoes");
        c.Publica.Should().BeFalse();
        c.AtualizadoEm.Should().BeAfter(antes);
    }
}

public class FaqItemTests
{
    [Fact]
    public void Criar_inicia_em_rascunho_e_zera_metricas()
    {
        var item = FaqItem.Criar(
            categoriaId: Guid.NewGuid(),
            titulo: "Como cadastrar produto",
            slug: "como-cadastrar-produto",
            conteudo: "# Conteudo\n\npasso a passo",
            tags: new[] { "produto", "cadastro" });

        item.Status.Should().Be(FaqStatus.Rascunho);
        item.Visualizacoes.Should().Be(0);
        item.UtilCount.Should().Be(0);
        item.NaoUtilCount.Should().Be(0);
        item.PublicadoEm.Should().BeNull();
        item.Tags.Should().BeEquivalentTo(new[] { "produto", "cadastro" });
        item.ConteudoBusca.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Publicar_define_publicadoEm_e_status()
    {
        var item = FaqItem.Criar(Guid.NewGuid(), "T", "t", "conteudo");

        item.Publicar();

        item.Status.Should().Be(FaqStatus.Publicado);
        item.PublicadoEm.Should().NotBeNull();
        item.PublicadoEm!.Value.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Publicar_idempotente_nao_atualiza_se_ja_publicado()
    {
        var item = FaqItem.Criar(Guid.NewGuid(), "T", "t", "conteudo");
        item.Publicar();
        var publicadoEm = item.PublicadoEm;

        Thread.Sleep(5);
        item.Publicar();

        item.PublicadoEm.Should().Be(publicadoEm);
    }

    [Fact]
    public void Arquivar_muda_status()
    {
        var item = FaqItem.Criar(Guid.NewGuid(), "T", "t", "conteudo");
        item.Publicar();

        item.Arquivar();

        item.Status.Should().Be(FaqStatus.Arquivado);
    }

    [Fact]
    public void RegistrarVisualizacao_incrementa()
    {
        var item = FaqItem.Criar(Guid.NewGuid(), "T", "t", "conteudo");

        item.RegistrarVisualizacao();
        item.RegistrarVisualizacao();

        item.Visualizacoes.Should().Be(2);
    }

    [Fact]
    public void RegistrarFeedback_util_e_nao_util_separados()
    {
        var item = FaqItem.Criar(Guid.NewGuid(), "T", "t", "conteudo");

        item.RegistrarFeedback(util: true);
        item.RegistrarFeedback(util: true);
        item.RegistrarFeedback(util: false);

        item.UtilCount.Should().Be(2);
        item.NaoUtilCount.Should().Be(1);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Criar_falha_se_titulo_vazio(string titulo)
    {
        var act = () => FaqItem.Criar(Guid.NewGuid(), titulo, "slug", "conteudo");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Criar_falha_se_conteudo_excede_20000()
    {
        var conteudoLongo = new string('a', 20_001);
        var act = () => FaqItem.Criar(Guid.NewGuid(), "Titulo", "slug", conteudoLongo);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Slug_e_normalizado_para_minusculo()
    {
        var item = FaqItem.Criar(Guid.NewGuid(), "T", "  COMO-FAZER  ", "conteudo");
        item.Slug.Should().Be("como-fazer");
    }
}

public class FaqVisualizacaoTests
{
    [Fact]
    public void Criar_exige_itemId_e_ipHash()
    {
        var act = () => FaqVisualizacao.Criar(Guid.Empty, "hash");
        act.Should().Throw<ArgumentException>();

        var act2 = () => FaqVisualizacao.Criar(Guid.NewGuid(), "");
        act2.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Criar_normaliza_termo_vazio_para_null()
    {
        var v = FaqVisualizacao.Criar(Guid.NewGuid(), "abc", "  ");
        v.Termo.Should().BeNull();
    }
}

public class FaqFeedbackTests
{
    [Fact]
    public void Criar_falha_se_comentario_excede_1000()
    {
        var act = () => FaqFeedback.Criar(Guid.NewGuid(), util: true, "iphash", new string('x', 1001));
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Criar_aceita_comentario_null()
    {
        var f = FaqFeedback.Criar(Guid.NewGuid(), util: false, "hash", null);
        f.Comentario.Should().BeNull();
        f.Util.Should().BeFalse();
    }
}
