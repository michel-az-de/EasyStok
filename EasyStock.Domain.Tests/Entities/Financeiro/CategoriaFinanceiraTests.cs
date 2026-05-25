using EasyStock.Domain.Entities.Financeiro;
using EasyStock.Domain.Enums.Financeiro;
using EasyStock.Domain.Exceptions;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities.Financeiro;

public class CategoriaFinanceiraTests
{
    private static readonly Guid Empresa = Guid.NewGuid();

    [Fact]
    public void Criar_categoria_raiz_define_profundidade_1()
    {
        var c = CategoriaFinanceira.Criar(Empresa, "Aluguel", TipoCategoriaFinanceira.Despesa);
        c.Id.Should().NotBeEmpty();
        c.Profundidade.Should().Be(1);
        c.ParentId.Should().BeNull();
        c.Ativa.Should().BeTrue();
    }

    [Fact]
    public void Criar_subcategoria_incrementa_profundidade()
    {
        var raiz = CategoriaFinanceira.Criar(Empresa, "Operacional", TipoCategoriaFinanceira.Despesa);
        var sub = CategoriaFinanceira.Criar(Empresa, "Aluguel", TipoCategoriaFinanceira.Despesa, raiz);
        sub.Profundidade.Should().Be(2);
        sub.ParentId.Should().Be(raiz.Id);
    }

    [Fact]
    public void Criar_rejeita_profundidade_acima_de_3()
    {
        var n1 = CategoriaFinanceira.Criar(Empresa, "Nivel1", TipoCategoriaFinanceira.Despesa);
        var n2 = CategoriaFinanceira.Criar(Empresa, "Nivel2", TipoCategoriaFinanceira.Despesa, n1);
        var n3 = CategoriaFinanceira.Criar(Empresa, "Nivel3", TipoCategoriaFinanceira.Despesa, n2);

        var act = () => CategoriaFinanceira.Criar(Empresa, "Nivel4", TipoCategoriaFinanceira.Despesa, n3);

        act.Should().Throw<RegraDeDominioVioladaException>().WithMessage("*Profundidade*");
    }

    [Fact]
    public void Criar_rejeita_parent_de_outra_empresa()
    {
        var outra = CategoriaFinanceira.Criar(Guid.NewGuid(), "OutraEmp", TipoCategoriaFinanceira.Despesa);
        var act = () => CategoriaFinanceira.Criar(Empresa, "Sub", TipoCategoriaFinanceira.Despesa, outra);
        act.Should().Throw<RegraDeDominioVioladaException>().WithMessage("*outra empresa*");
    }

    [Fact]
    public void Criar_rejeita_subcategoria_com_tipo_diferente_do_pai()
    {
        var pai = CategoriaFinanceira.Criar(Empresa, "Receita", TipoCategoriaFinanceira.Receita);
        var act = () => CategoriaFinanceira.Criar(Empresa, "Sub", TipoCategoriaFinanceira.Despesa, pai);
        act.Should().Throw<RegraDeDominioVioladaException>().WithMessage("*Tipo*");
    }

    [Fact]
    public void Criar_aceita_subcategoria_quando_pai_e_Ambas()
    {
        var pai = CategoriaFinanceira.Criar(Empresa, "Geral", TipoCategoriaFinanceira.Ambas);
        var sub = CategoriaFinanceira.Criar(Empresa, "Sub", TipoCategoriaFinanceira.Despesa, pai);
        sub.Tipo.Should().Be(TipoCategoriaFinanceira.Despesa);
    }

    [Fact]
    public void Criar_rejeita_nome_vazio()
    {
        var act = () => CategoriaFinanceira.Criar(Empresa, "  ", TipoCategoriaFinanceira.Despesa);
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void MoverPara_rejeita_si_mesmo()
    {
        var c = CategoriaFinanceira.Criar(Empresa, "X", TipoCategoriaFinanceira.Despesa);
        var act = () => c.MoverPara(c);
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void Inativar_e_idempotente()
    {
        var c = CategoriaFinanceira.Criar(Empresa, "X", TipoCategoriaFinanceira.Despesa);
        c.Inativar();
        var antes = c.AlteradoEm;
        c.Inativar();
        c.AlteradoEm.Should().Be(antes);
        c.Ativa.Should().BeFalse();
    }
}
