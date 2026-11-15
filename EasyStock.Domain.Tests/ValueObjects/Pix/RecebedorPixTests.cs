using EasyStock.Domain.Enums.Pagamentos;
using EasyStock.Domain.Exceptions;
using EasyStock.Domain.ValueObjects.Pix;
using FluentAssertions;

namespace EasyStock.Domain.Tests.ValueObjects.Pix;

/// <summary>#2233: nome até 25 e cidade até 15 caracteres, sem acento, como o BR Code exige.</summary>
public class RecebedorPixTests
{
    private static readonly ChavePix Chave = ChavePix.Criar(TipoChavePix.Email, "loja@exemplo.test");

    [Fact]
    public void Tira_acento_e_espaco_sobrando()
    {
        var recebedor = RecebedorPix.Criar(Chave, "  Confeitaria  do Zé ", " São Paulo ");

        recebedor.Nome.Should().Be("Confeitaria do Ze");
        recebedor.Cidade.Should().Be("Sao Paulo");
    }

    [Fact]
    public void Nome_ate_25_e_cidade_ate_15_passam()
    {
        var recebedor = RecebedorPix.Criar(Chave, new string('N', 25), new string('C', 15));

        recebedor.Nome.Should().HaveLength(25);
        recebedor.Cidade.Should().HaveLength(15);
    }

    [Theory]
    [InlineData(26, 15)]
    [InlineData(25, 16)]
    [InlineData(0, 5)]
    [InlineData(5, 0)]
    public void Nome_ou_cidade_fora_do_limite_e_recusado(int nome, int cidade)
    {
        var acao = () => RecebedorPix.Criar(Chave, new string('N', nome), new string('C', cidade));

        acao.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void Caractere_que_nao_vira_ASCII_e_recusado()
    {
        var acao = () => RecebedorPix.Criar(Chave, "Loja 漢字", "Sao Paulo");

        acao.Should().Throw<RegraDeDominioVioladaException>();
    }
}
