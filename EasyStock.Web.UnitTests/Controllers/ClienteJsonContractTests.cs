using EasyStock.Web.Models.Api;
using FluentAssertions;

namespace EasyStock.Web.UnitTests.Controllers;

/// <summary>
/// Contrato do cliente no BFF (#1018). O Web é BFF puro: campo que não existe no modelo é
/// descartado em silêncio na desserialização, e o cadastro "salva com sucesso" com o dado
/// sumindo. Estes testes travam o shape que a Api devolve e como o Web o interpreta.
/// </summary>
public class ClienteJsonContractTests
{
    private static Cliente Novo(string tipoPessoa = "fisica", string? nomeFantasia = null) => new()
    {
        Id = Guid.NewGuid().ToString(),
        Nome = "Distribuidora Sul Ltda",
        TipoPessoa = tipoPessoa,
        NomeFantasia = nomeFantasia,
        Ativo = true,
    };

    [Fact]
    public void Cliente_sem_tipo_informado_e_pessoa_fisica()
    {
        // Resposta de Api antiga (ou cliente criado antes da migration) não traz o campo.
        var c = new Cliente { Id = "1", Nome = "Ana", Ativo = true };

        c.TipoPessoa.Should().Be("fisica");
        c.EhPessoaJuridica.Should().BeFalse();
    }

    [Fact]
    public void Pessoa_juridica_e_reconhecida()
    {
        Novo("juridica").EhPessoaJuridica.Should().BeTrue();
    }

    [Fact]
    public void Comparacao_de_tipo_ignora_caixa()
    {
        Novo("Juridica").EhPessoaJuridica.Should().BeTrue();
    }

    [Fact]
    public void Nome_de_exibicao_prefere_o_fantasia_quando_existe()
    {
        // Para PJ, é pelo fantasia que o operador reconhece a empresa — a razão social
        // raramente é o que ele lembra.
        Novo("juridica", "Sul Bebidas").NomeExibicao.Should().Be("Sul Bebidas");
    }

    [Fact]
    public void Nome_de_exibicao_cai_no_nome_quando_nao_ha_fantasia()
    {
        Novo("juridica").NomeExibicao.Should().Be("Distribuidora Sul Ltda");
        Novo("juridica", "   ").NomeExibicao.Should().Be("Distribuidora Sul Ltda");
    }

    [Fact]
    public void Pessoa_fisica_exibe_o_proprio_nome()
    {
        var c = new Cliente { Id = "1", Nome = "Ana Beatriz", Ativo = true };

        c.NomeExibicao.Should().Be("Ana Beatriz");
    }
}
