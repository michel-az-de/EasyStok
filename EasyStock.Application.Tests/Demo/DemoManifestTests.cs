using EasyStock.Application.Demo;

namespace EasyStock.Application.Tests.Demo;

/// <summary>
/// Prova a propriedade que sustenta a seguranca do "limpar" da loja-demo: os Ids
/// sao deterministicos (estaveis por empresa+slot), variam por empresa e por slot,
/// e nao colidem entre tipos. Isso garante que "limpar" (que apaga por esses Ids)
/// nunca atinge uma linha real, cujo Id e sempre um Guid.NewGuid aleatorio.
/// </summary>
public class DemoManifestTests
{
    [Fact]
    public void Id_EhEstavel_ParaMesmaEmpresaESlot()
    {
        var empresa = Guid.NewGuid();
        DemoManifest.Id(empresa, "produto-1").Should().Be(DemoManifest.Id(empresa, "produto-1"));
    }

    [Fact]
    public void Id_VariaPorEmpresa()
    {
        DemoManifest.Id(Guid.NewGuid(), "produto-1")
            .Should().NotBe(DemoManifest.Id(Guid.NewGuid(), "produto-1"));
    }

    [Fact]
    public void Id_VariaPorSlot()
    {
        var empresa = Guid.NewGuid();
        DemoManifest.Id(empresa, "produto-1").Should().NotBe(DemoManifest.Id(empresa, "produto-2"));
    }

    [Fact]
    public void Id_NuncaEhVazio()
    {
        DemoManifest.Id(Guid.NewGuid(), "produto-1").Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void ProdutoId_NaoColide_ComOutrosTipos()
    {
        var empresa = Guid.NewGuid();
        var produto = DemoManifest.ProdutoId(empresa, 1);
        produto.Should().NotBe(DemoManifest.CategoriaId(empresa, 1));
        produto.Should().NotBe(DemoManifest.ItemEstoqueId(empresa, 1));
        produto.Should().NotBe(DemoManifest.EntradaId(empresa, 1));
        produto.Should().NotBe(DemoManifest.VendaId(empresa, 1));
    }

    [Fact]
    public void TodosOsIds_SaoUnicos_SemVazio()
    {
        var empresa = Guid.NewGuid();
        var ids = DemoManifest.TodosOsIds(empresa);
        ids.Should().NotContain(Guid.Empty);
        // 4 categorias + 12 produtos x 4 linhas (produto/item/entrada/venda).
        ids.Count.Should().Be(DemoManifest.Categorias.Count + DemoManifest.Produtos.Count * 4);
    }
}
