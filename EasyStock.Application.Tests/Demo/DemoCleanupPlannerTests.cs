using EasyStock.Application.Demo;

namespace EasyStock.Application.Tests.Demo;

/// <summary>
/// Prova a invariante de seguranca do "limpar" da loja-demo: nunca apaga dado real.
/// Cobre os casos que os portoes do plano cobraram (categoria presa por produto,
/// movimento/venda real sobre item demo): qualquer Id referenciado por linha viva
/// e preservado, e Id fora do manifesto nunca e tocado.
/// </summary>
public class DemoCleanupPlannerTests
{
    [Fact]
    public void Apaga_SoIds_DoManifesto()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var manifesto = new HashSet<Guid> { a, b };

        var plan = DemoCleanupPlanner.Plan(new DemoCleanupRequest(manifesto, new HashSet<Guid>()));

        plan.Apagar.Should().OnlyContain(id => manifesto.Contains(id));
        plan.Apagar.Should().HaveCount(2);
        plan.Preservar.Should().BeEmpty();
    }

    [Fact]
    public void Preserva_IdDoManifesto_ReferenciadoPorLinhaViva()
    {
        var produtoUsadoEmVendaReal = Guid.NewGuid();
        var produtoIntocado = Guid.NewGuid();
        var manifesto = new HashSet<Guid> { produtoUsadoEmVendaReal, produtoIntocado };
        var comReferenciaViva = new HashSet<Guid> { produtoUsadoEmVendaReal };

        var plan = DemoCleanupPlanner.Plan(new DemoCleanupRequest(manifesto, comReferenciaViva));

        plan.Apagar.Should().NotContain(produtoUsadoEmVendaReal);
        plan.Apagar.Should().Contain(produtoIntocado);
        plan.Preservar.Should().Contain(produtoUsadoEmVendaReal);
    }

    [Fact]
    public void Id_ForaDoManifesto_NuncaEntra_EmApagar_NemPreservar()
    {
        var demo = Guid.NewGuid();
        var real = Guid.NewGuid(); // nao pertence ao manifesto

        var plan = DemoCleanupPlanner.Plan(
            new DemoCleanupRequest(new HashSet<Guid> { demo }, new HashSet<Guid> { real }));

        plan.Apagar.Should().NotContain(real);
        plan.Preservar.Should().NotContain(real);
        plan.Apagar.Should().Contain(demo);
    }

    [Fact]
    public void StoreIntocada_ApagaTodoOManifesto()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        var manifesto = new HashSet<Guid> { a, b, c };

        var plan = DemoCleanupPlanner.Plan(new DemoCleanupRequest(manifesto, new HashSet<Guid>()));

        plan.Apagar.Should().HaveCount(3);
        plan.Preservar.Should().BeEmpty();
    }
}
