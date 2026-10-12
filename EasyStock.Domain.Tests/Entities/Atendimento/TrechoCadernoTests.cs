using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Exceptions;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities.Atendimento;

/// <summary>S54: trecho do caderno da loja que o agente lê pelo índice ou pelo núcleo.</summary>
public class TrechoCadernoTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly DateTime Agora = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void CriaComCamposLimposECodigoCurto()
    {
        var trecho = TrechoCaderno.Criar(Empresa, "  Política de troca ", " Trocamos em até 24 h. ", "troca, Devolução", nucleo: true, Agora);

        trecho.Titulo.Should().Be("Política de troca");
        trecho.Texto.Should().Be("Trocamos em até 24 h.");
        trecho.Nucleo.Should().BeTrue();
        trecho.Arquivado.Should().BeFalse();
        trecho.Codigo.Should().HaveLength(8).And.Be(trecho.Id.ToString("N")[..8]);
        trecho.CriadoEm.Should().Be(Agora);
    }

    [Theory]
    [InlineData(" Troca ; DEVOLUÇÃO,troca,, reembolso ", "troca, devolução, reembolso")]
    [InlineData(null, "")]
    [InlineData("   ", "")]
    public void NormalizaPalavrasChave(string? entrada, string esperado) =>
        TrechoCaderno.NormalizarPalavrasChave(entrada).Should().Be(esperado);

    [Theory]
    [InlineData("", "texto")]
    [InlineData("título", "")]
    public void RecusaTituloOuTextoVazio(string titulo, string texto)
    {
        var acao = () => TrechoCaderno.Criar(Empresa, titulo, texto, null, nucleo: false, Agora);

        acao.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void RecusaTextoAcimaDoLimite()
    {
        var acao = () => TrechoCaderno.Criar(Empresa, "t", new string('a', TrechoCaderno.TextoTamanhoMaximo + 1), null, false, Agora);

        acao.Should().Throw<RegraDeDominioVioladaException>().WithMessage("*Texto*");
    }

    [Fact]
    public void RecusaPalavrasChaveAcimaDoLimite()
    {
        var acao = () => TrechoCaderno.Criar(Empresa, "t", "x", new string('a', TrechoCaderno.PalavrasChaveTamanhoMaximo + 1), false, Agora);

        acao.Should().Throw<RegraDeDominioVioladaException>().WithMessage("*Palavras-chave*");
    }

    [Fact]
    public void EditaEArquiva()
    {
        var trecho = TrechoCaderno.Criar(Empresa, "Entrega", "Entregamos de terça a sábado.", "entrega", false, Agora);

        trecho.Editar("Entrega e retirada", "Entregamos de terça a sábado; retirada na loja.", "entrega, retirada", true, Agora.AddHours(1));
        trecho.Arquivar(true, Agora.AddHours(2));

        trecho.Titulo.Should().Be("Entrega e retirada");
        trecho.PalavrasChave.Should().Be("entrega, retirada");
        trecho.Nucleo.Should().BeTrue();
        trecho.Arquivado.Should().BeTrue();
        trecho.AlteradoEm.Should().Be(Agora.AddHours(2));
    }

    [Fact]
    public void RecusaEmpresaVazia()
    {
        var acao = () => TrechoCaderno.Criar(Guid.Empty, "t", "x", null, false, Agora);

        acao.Should().Throw<RegraDeDominioVioladaException>();
    }
}
