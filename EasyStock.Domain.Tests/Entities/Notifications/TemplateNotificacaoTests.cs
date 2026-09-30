using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities.Notifications;

public class TemplateNotificacaoTests
{
    [Fact]
    public void Criar_define_versao_1_inativo_e_nao_aprovado()
    {
        var t = TemplateNotificacao.Criar(
            "produto_vencendo_email", "Produto vencendo (email)",
            CanalNotificacao.Email, TipoEventoNotificacao.ProdutoVencendo,
            "{{produto.nome}} vencendo", "<p>Olá</p>");

        t.Versao.Should().Be(1);
        t.Ativo.Should().BeFalse();
        t.Aprovado.Should().BeFalse();
        t.ChecksumSha256.Should().NotBeEmpty();
        t.CriadoEm.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void AtualizarConteudo_invalida_aprovacao_e_recomputa_checksum()
    {
        var t = TemplateNotificacao.Criar("c", "n", CanalNotificacao.Email, TipoEventoNotificacao.ResetSenha, "s", "b");
        t.Aprovar("admin@x.com");
        t.Ativar();
        var checksumOriginal = t.ChecksumSha256;

        t.AtualizarConteudo("novo", "<p>novo</p>", "felipe@x.com");

        t.Aprovado.Should().BeFalse();
        t.Ativo.Should().BeFalse();
        t.ChecksumSha256.Should().NotBe(checksumOriginal);
        t.AtualizadoPor.Should().Be("felipe@x.com");
    }

    [Fact]
    public void Ativar_sem_aprovacao_lanca_excecao()
    {
        var t = TemplateNotificacao.Criar("c", "n", CanalNotificacao.Email, TipoEventoNotificacao.ResetSenha, "s", "b");

        var ex = Record.Exception(() => t.Ativar());

        ex.Should().BeOfType<InvalidOperationException>();
        ex!.Message.Should().Contain("aprovado");
    }

    [Fact]
    public void Ativar_apos_aprovar_funciona()
    {
        var t = TemplateNotificacao.Criar("c", "n", CanalNotificacao.Email, TipoEventoNotificacao.ResetSenha, "s", "b");
        t.Aprovar("admin@x.com");

        t.Ativar();

        t.Ativo.Should().BeTrue();
    }

    [Fact]
    public void Checksum_muda_quando_corpo_muda()
    {
        var a = TemplateNotificacao.Criar("c", "n", CanalNotificacao.Email, TipoEventoNotificacao.ResetSenha, "s", "<p>A</p>");
        var b = TemplateNotificacao.Criar("c", "n", CanalNotificacao.Email, TipoEventoNotificacao.ResetSenha, "s", "<p>B</p>");

        a.ChecksumSha256.Should().NotBe(b.ChecksumSha256);
    }

    [Fact]
    public void DefinirMetadados_guarda_o_json_e_vazio_vira_nulo()
    {
        var t = TemplateNotificacao.Criar("pedido_em_preparo_whatsapp_v1", "Em preparo", CanalNotificacao.WhatsApp,
            TipoEventoNotificacao.FaturaVencida, "", "corpo");

        t.DefinirMetadados("""{"template":"pedido_em_preparo"}""");
        t.MetadadosJson.Should().Be("""{"template":"pedido_em_preparo"}""");

        t.DefinirMetadados("  ");
        t.MetadadosJson.Should().BeNull();
    }
}
