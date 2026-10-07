using EasyStock.Application.Services.Atendimento.Email;
using EasyStock.Domain.Entities.Atendimento;

namespace EasyStock.Application.Tests.Services.Atendimento.Email;

/// <summary>#1432: assunto, corpo e id externo do e-mail do atendimento.</summary>
public class TextoEmailAtendimentoTests
{
    [Theory]
    [InlineData("Encomenda de bolo", "Encomenda de bolo")]
    [InlineData("Re: Encomenda de bolo", "Encomenda de bolo")]
    [InlineData("RES: RE: Fwd: Encomenda de bolo", "Encomenda de bolo")]
    [InlineData("ENC: Re[2]: Encomenda", "Encomenda")]
    [InlineData("  Re:   ", null)]
    [InlineData(null, null)]
    public void SemPrefixos_TiraReEFwdRepetidos(string? bruto, string? esperado) =>
        AssuntoEmail.SemPrefixos(bruto).Should().Be(esperado);

    [Fact]
    public void Resposta_PoeReUmaVezSo_ESemAssuntoUsaOPadrao()
    {
        AssuntoEmail.Resposta("Re: Encomenda de bolo").Should().Be("Re: Encomenda de bolo");
        AssuntoEmail.Resposta("Encomenda").Should().Be("Re: Encomenda");
        AssuntoEmail.Resposta(null).Should().Be(AssuntoEmail.Padrao);
    }

    [Fact]
    public void Corpo_CortaOHistoricoDoGmailEmDuasLinhas()
    {
        var texto = "Quero 2 lasanhas para sábado.\r\nObrigada!\r\n\r\nEm qua., 7 de out. de 2026 às 10:00, Casa da Baba <contato@casadababa.com>\r\nescreveu:\r\n> Olá, tudo bem?\r\n";

        CorpoEmail.Limpar(texto).Should().Be("Quero 2 lasanhas para sábado.\nObrigada!");
    }

    [Theory]
    [InlineData("Pode ser às 18h.\n\nOn Wed, Oct 7, 2026 at 10:00 AM Loja <a@b.com> wrote:\n> oi")]
    [InlineData("Pode ser às 18h.\n-----Mensagem original-----\nDe: Loja\nEnviada em: hoje")]
    [InlineData("Pode ser às 18h.\n________________________________\nDe: Casa da Baba <contato@casadababa.com>\nEnviado: quarta-feira\nPara: Maria")]
    public void Corpo_CortaHistoricoDoOutlookEEmIngles(string texto) =>
        CorpoEmail.Limpar(texto).Should().Be("Pode ser às 18h.");

    [Fact]
    public void Corpo_SoCitacaoFicaInteiro_EVazioViraNulo()
    {
        CorpoEmail.Limpar("> só a citação").Should().Be("> só a citação");
        CorpoEmail.Limpar("  \n ").Should().BeNull();
    }

    [Fact]
    public void Corpo_LongoCabeNoTetoDaMensagem()
    {
        var limpo = CorpoEmail.Limpar(new string('a', Mensagem.TextoTamanhoMaximo + 500));

        limpo!.Length.Should().Be(Mensagem.TextoTamanhoMaximo);
        limpo.Should().EndWith("…");
    }

    [Fact]
    public void IdExterno_GuardaOMessageIdSemSinais_EOLongoViraHash()
    {
        IdExternoEmail.Principal("<abc@mail.gmail.com>", "maria@x.com", "7", "a", "b").Should().Be("abc@mail.gmail.com");
        var longo = IdExternoEmail.Principal(new string('x', 200) + "@y", "maria@x.com", "7", "a", "b");
        longo.Should().StartWith("h:").And.HaveLength(66);
        IdExternoEmail.ServeParaEncadear(longo).Should().BeFalse();
        IdExternoEmail.ServeParaEncadear("abc@mail.gmail.com").Should().BeTrue();
    }

    [Fact]
    public void IdExterno_SemMessageIdEhEstavel_EAnexoNuncaEncadeia()
    {
        var a = IdExternoEmail.Principal(null, "maria@x.com", "7", "Bolo", "texto");
        IdExternoEmail.Principal("  ", "maria@x.com", "7", "Bolo", "texto").Should().Be(a, "a mesma releitura dá o mesmo id");
        IdExternoEmail.Principal(null, "maria@x.com", "8", "Bolo", "texto").Should().NotBe(a, "outro e-mail igual na caixa");
        a.Should().StartWith("sem-id:");
        IdExternoEmail.ServeParaEncadear(a).Should().BeFalse();

        var anexo = IdExternoEmail.Anexo("abc@mail.gmail.com", 1);
        anexo.Should().NotBe(IdExternoEmail.Anexo("abc@mail.gmail.com", 2));
        anexo.Length.Should().BeLessThanOrEqualTo(Mensagem.ExternoIdTamanhoMaximo);
        IdExternoEmail.ServeParaEncadear(anexo).Should().BeFalse();
    }

    [Fact]
    public void Dominio_AssuntoDaConversaNaoSomeComRespostaSemAssunto()
    {
        var conversa = Conversa.Abrir(Guid.NewGuid(), "Maria@X.com", DateTime.UtcNow, canal: Domain.Enums.Atendimento.CanalConversa.Email);

        conversa.DefinirAssunto("Encomenda\r\n de bolo");
        conversa.DefinirAssunto("   ");

        conversa.Assunto.Should().Be("Encomenda de bolo");
        conversa.ContatoIdExterno.Should().Be("maria@x.com");
        Mensagem.NormalizarAssunto(new string('a', 400))!.Length.Should().Be(Mensagem.AssuntoTamanhoMaximo);
    }
}
