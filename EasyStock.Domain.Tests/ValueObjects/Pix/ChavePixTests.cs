using EasyStock.Domain.Enums.Pagamentos;
using EasyStock.Domain.Exceptions;
using EasyStock.Domain.ValueObjects.Pix;
using FluentAssertions;

namespace EasyStock.Domain.Tests.ValueObjects.Pix;

/// <summary>#2233: a chave Pix da loja é validada pelo tipo e nunca aparece em texto de log ou de erro.</summary>
public class ChavePixTests
{
    [Theory]
    [InlineData(TipoChavePix.Cpf, "529.982.247-25", "52998224725")]
    [InlineData(TipoChavePix.Cnpj, "11.222.333/0001-81", "11222333000181")]
    [InlineData(TipoChavePix.Email, " Loja@Exemplo.TEST ", "loja@exemplo.test")]
    [InlineData(TipoChavePix.Telefone, "(61) 91234-5678", "+5561912345678")]
    [InlineData(TipoChavePix.Telefone, "+55 61 91234-5678", "+5561912345678")]
    [InlineData(TipoChavePix.Telefone, "6132345678", "+556132345678")]
    [InlineData(TipoChavePix.Aleatoria, "123E4567-E12B-12D1-A456-426655440000", "123e4567-e12b-12d1-a456-426655440000")]
    public void Normaliza_a_chave_valida_de_cada_tipo(TipoChavePix tipo, string entrada, string esperado)
    {
        ChavePix.Criar(tipo, entrada).Valor.Should().Be(esperado);
    }

    [Theory]
    [InlineData(TipoChavePix.Cpf, "529.982.247-26")]       // dígito errado
    [InlineData(TipoChavePix.Cpf, "111.111.111-11")]
    [InlineData(TipoChavePix.Cpf, "11.222.333/0001-81")]   // CNPJ no tipo CPF
    [InlineData(TipoChavePix.Cnpj, "11.222.333/0001-82")]
    [InlineData(TipoChavePix.Cnpj, "529.982.247-25")]      // CPF no tipo CNPJ
    [InlineData(TipoChavePix.Email, "sem-arroba.test")]
    [InlineData(TipoChavePix.Telefone, "91234")]
    [InlineData(TipoChavePix.Telefone, "+1 202 555 0100")]
    [InlineData(TipoChavePix.Aleatoria, "nao-e-uuid")]
    [InlineData(TipoChavePix.Email, "")]
    public void Recusa_chave_que_nao_bate_com_o_tipo(TipoChavePix tipo, string entrada)
    {
        var acao = () => ChavePix.Criar(tipo, entrada);

        acao.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void Email_acima_de_77_caracteres_e_recusado()
    {
        var acao = () => ChavePix.Criar(TipoChavePix.Email, new string('a', 70) + "@exemplo.test");

        acao.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void Mensagem_de_erro_nao_repete_a_chave()
    {
        var acao = () => ChavePix.Criar(TipoChavePix.Email, "segredo-sem-arroba.test");

        acao.Should().Throw<RegraDeDominioVioladaException>().Which.Message.Should().NotContain("segredo");
    }

    [Fact]
    public void Texto_da_chave_e_do_recebedor_nao_expoe_o_valor()
    {
        var chave = ChavePix.Criar(TipoChavePix.Email, "loja@exemplo.test");
        var recebedor = RecebedorPix.Criar(chave, "Loja Exemplo", "Sao Paulo");

        chave.ToString().Should().NotContain("loja@exemplo.test");
        recebedor.ToString().Should().NotContain("loja@exemplo.test");
        $"{recebedor}".Should().NotContain("exemplo");
    }
}
