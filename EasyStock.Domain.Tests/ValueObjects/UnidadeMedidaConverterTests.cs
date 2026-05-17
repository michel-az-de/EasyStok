using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;
using FluentAssertions;

namespace EasyStock.Domain.Tests.ValueObjects;

public class UnidadeMedidaConverterTests
{
    [Fact]
    public void Identidade_retorna_mesmo_valor()
    {
        var (v, e) = UnidadeMedidaConverter.Converter(123.456m, UnidadeMedida.Kg, UnidadeMedida.Kg);
        v.Should().Be(123.456m);
        e.Should().BeNull();
    }

    [Fact]
    public void G_para_Kg_converte_dividindo_por_1000()
    {
        var (v, _) = UnidadeMedidaConverter.Converter(2500m, UnidadeMedida.G, UnidadeMedida.Kg);
        v.Should().Be(2.5m);
    }

    [Fact]
    public void Kg_para_G_converte_multiplicando_por_1000()
    {
        var (v, _) = UnidadeMedidaConverter.Converter(0.3m, UnidadeMedida.Kg, UnidadeMedida.G);
        v.Should().Be(300m);
    }

    [Fact]
    public void Mg_para_Kg_funciona_encadeado_via_g()
    {
        var (v, _) = UnidadeMedidaConverter.Converter(500_000m, UnidadeMedida.Mg, UnidadeMedida.Kg);
        v.Should().Be(0.5m);
    }

    [Fact]
    public void Ml_para_L_converte_corretamente()
    {
        var (v, _) = UnidadeMedidaConverter.Converter(750m, UnidadeMedida.Ml, UnidadeMedida.L);
        v.Should().Be(0.75m);
    }

    [Fact]
    public void Dz_para_Un_e_12_unidades()
    {
        var (v, _) = UnidadeMedidaConverter.Converter(2m, UnidadeMedida.Dz, UnidadeMedida.Un);
        v.Should().Be(24m);
    }

    [Fact]
    public void Un_para_Dz_e_um_doze_avos()
    {
        var (v, _) = UnidadeMedidaConverter.Converter(24m, UnidadeMedida.Un, UnidadeMedida.Dz);
        v.Should().Be(2m);
    }

    [Fact]
    public void Un_para_G_e_incompativel()
    {
        var (v, erro) = UnidadeMedidaConverter.Converter(10m, UnidadeMedida.Un, UnidadeMedida.G);
        v.Should().BeNull();
        erro.Should().Contain("incompativeis", because: "grupos diferentes (Contagem vs Massa)");
    }

    [Fact]
    public void Kg_para_Ml_e_incompativel()
    {
        var (v, _) = UnidadeMedidaConverter.Converter(1m, UnidadeMedida.Kg, UnidadeMedida.Ml);
        v.Should().BeNull();
    }

    [Fact]
    public void Cx_nunca_converte_para_qualquer_unidade()
    {
        var (vSaindo, _) = UnidadeMedidaConverter.Converter(1m, UnidadeMedida.Cx, UnidadeMedida.Un);
        var (vEntrando, _) = UnidadeMedidaConverter.Converter(10m, UnidadeMedida.Un, UnidadeMedida.Cx);
        vSaindo.Should().BeNull();
        vEntrando.Should().BeNull();
    }

    [Fact]
    public void Cx_para_Cx_e_identidade()
    {
        var (v, e) = UnidadeMedidaConverter.Converter(5m, UnidadeMedida.Cx, UnidadeMedida.Cx);
        v.Should().Be(5m);
        e.Should().BeNull();
    }

    [Fact]
    public void Round_trip_g_kg_g_preserva_valor()
    {
        var (kg, _) = UnidadeMedidaConverter.Converter(2500m, UnidadeMedida.G, UnidadeMedida.Kg);
        var (deVolta, _) = UnidadeMedidaConverter.Converter(kg!.Value, UnidadeMedida.Kg, UnidadeMedida.G);
        deVolta.Should().Be(2500m);
    }

    [Fact]
    public void UnidadesCompativeis_Un_retorna_apenas_Contagem()
    {
        var compativeis = UnidadeMedidaConverter.UnidadesCompativeis(UnidadeMedida.Un);
        compativeis.Should().BeEquivalentTo(new[] { UnidadeMedida.Un, UnidadeMedida.Dz });
    }

    [Fact]
    public void UnidadesCompativeis_Kg_retorna_apenas_Massa()
    {
        var compativeis = UnidadeMedidaConverter.UnidadesCompativeis(UnidadeMedida.Kg);
        compativeis.Should().BeEquivalentTo(new[] { UnidadeMedida.Mg, UnidadeMedida.G, UnidadeMedida.Kg });
    }

    [Fact]
    public void UnidadesCompativeis_Cx_retorna_apenas_Cx()
    {
        var compativeis = UnidadeMedidaConverter.UnidadesCompativeis(UnidadeMedida.Cx);
        compativeis.Should().BeEquivalentTo(new[] { UnidadeMedida.Cx });
    }

    [Fact]
    public void GetGrupo_classifica_corretamente()
    {
        UnidadeMedidaConverter.GetGrupo(UnidadeMedida.Kg).Should().Be(UnidadeMedidaConverter.GrupoUnidade.Massa);
        UnidadeMedidaConverter.GetGrupo(UnidadeMedida.L).Should().Be(UnidadeMedidaConverter.GrupoUnidade.Volume);
        UnidadeMedidaConverter.GetGrupo(UnidadeMedida.Un).Should().Be(UnidadeMedidaConverter.GrupoUnidade.Contagem);
        UnidadeMedidaConverter.GetGrupo(UnidadeMedida.Cx).Should().BeNull(because: "Cx esta fora do agrupamento — nao converte automaticamente");
    }
}
