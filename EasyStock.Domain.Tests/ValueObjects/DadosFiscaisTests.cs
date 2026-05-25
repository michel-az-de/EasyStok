using EasyStock.Domain.ValueObjects;
using FluentAssertions;

namespace EasyStock.Domain.Tests.ValueObjects;

public class DadosFiscaisTests
{
    [Fact]
    public void Construtor_padrao_inicializa_tudo_em_null_e_iss_retido_false()
    {
        var fiscais = new DadosFiscais();

        fiscais.CodigoServico.Should().BeNull();
        fiscais.AliquotaIss.Should().BeNull();
        fiscais.IssRetido.Should().BeFalse();
        fiscais.NaturezaOperacao.Should().BeNull();
        fiscais.RetencaoIr.Should().BeNull();
        fiscais.RetencaoCsll.Should().BeNull();
        fiscais.RetencaoPis.Should().BeNull();
        fiscais.RetencaoCofins.Should().BeNull();
        fiscais.RetencaoInss.Should().BeNull();
        fiscais.DescricaoServico.Should().BeNull();
        fiscais.SchemaVersao.Should().Be(DadosFiscais.SchemaVersaoAtual);
    }

    [Fact]
    public void SchemaVersaoAtual_e_um()
    {
        DadosFiscais.SchemaVersaoAtual.Should().Be(1);
    }

    [Fact]
    public void Construtor_preserva_aliquotas_e_retencoes_quando_fornecidos()
    {
        var fiscais = new DadosFiscais(
            CodigoServico: "1.05",
            AliquotaIss: 5.0m,
            IssRetido: true,
            NaturezaOperacao: "Servico de TI",
            RetencaoIr: 1.5m,
            RetencaoCsll: 1.0m,
            RetencaoPis: 0.65m,
            RetencaoCofins: 3.0m,
            RetencaoInss: 11.0m,
            DescricaoServico: "Licenca SaaS mensal");

        fiscais.CodigoServico.Should().Be("1.05");
        fiscais.AliquotaIss.Should().Be(5.0m);
        fiscais.IssRetido.Should().BeTrue();
        fiscais.NaturezaOperacao.Should().Be("Servico de TI");
        fiscais.RetencaoIr.Should().Be(1.5m);
        fiscais.RetencaoCsll.Should().Be(1.0m);
        fiscais.RetencaoPis.Should().Be(0.65m);
        fiscais.RetencaoCofins.Should().Be(3.0m);
        fiscais.RetencaoInss.Should().Be(11.0m);
        fiscais.DescricaoServico.Should().Be("Licenca SaaS mensal");
    }

    [Fact]
    public void Equality_e_estrutural_para_records_com_campos_iguais()
    {
        var a = new DadosFiscais(CodigoServico: "1.05", AliquotaIss: 5.0m);
        var b = new DadosFiscais(CodigoServico: "1.05", AliquotaIss: 5.0m);

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void Equality_diferencia_quando_iss_retido_diverge()
    {
        var a = new DadosFiscais(IssRetido: true);
        var b = new DadosFiscais(IssRetido: false);

        a.Should().NotBe(b);
    }

    [Fact]
    public void SchemaVersao_pode_ser_sobrescrito()
    {
        var fiscais = new DadosFiscais(SchemaVersao: 2);
        fiscais.SchemaVersao.Should().Be(2);
    }
}
