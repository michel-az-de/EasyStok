using System.Security.Cryptography;

namespace EasyStock.Application.Demo;

/// <summary>Uma categoria do conjunto de demonstração.</summary>
public sealed record DemoCategoria(int Slot, string Nome);

/// <summary>Um produto do conjunto de demonstração (custo/preço em reais).</summary>
public sealed record DemoProduto(int Slot, int CategoriaSlot, string Nome, decimal Custo, decimal Preco);

/// <summary>
/// Manifesto da loja de demonstração (issue da loja-demo).
///
/// Os Ids (PK) das linhas demo são DETERMINÍSTICOS, derivados do empresaId:
/// um GUID estável computado de SHA256(empresaId || slot). Como o usuário nunca
/// escolhe a PK de uma linha, a chance de uma linha real colidir com um Id do
/// manifesto é nula, então "limpar" reconhece o que é demo sem marcador no
/// schema e sem risco de apagar dado real. Função pura: testável sem banco.
/// </summary>
public static class DemoManifest
{
    public const string Namespace = "ES-DEMO";

    /// <summary>GUID determinístico e estável para (empresa, slot).</summary>
    public static Guid Id(Guid empresaId, string slot)
    {
        var slotBytes = System.Text.Encoding.UTF8.GetBytes($"{Namespace}:{slot}");
        var seed = new byte[16 + slotBytes.Length];
        empresaId.ToByteArray().CopyTo(seed, 0);
        slotBytes.CopyTo(seed, 16);
        var hash = SHA256.HashData(seed);
        return new Guid(hash.AsSpan(0, 16).ToArray());
    }

    public static Guid CategoriaId(Guid empresaId, int slot) => Id(empresaId, $"categoria-{slot}");
    public static Guid ProdutoId(Guid empresaId, int slot) => Id(empresaId, $"produto-{slot}");
    public static Guid ItemEstoqueId(Guid empresaId, int slot) => Id(empresaId, $"item-estoque-{slot}");
    public static Guid EntradaId(Guid empresaId, int slot) => Id(empresaId, $"entrada-{slot}");
    public static Guid VendaId(Guid empresaId, int slot) => Id(empresaId, $"venda-{slot}");

    public static IReadOnlyList<DemoCategoria> Categorias { get; } = new[]
    {
        new DemoCategoria(1, "Bebidas"),
        new DemoCategoria(2, "Mercearia"),
        new DemoCategoria(3, "Limpeza"),
        new DemoCategoria(4, "Padaria"),
    };

    public static IReadOnlyList<DemoProduto> Produtos { get; } = new[]
    {
        new DemoProduto(1, 1, "Refrigerante Cola 2L", 4.50m, 8.90m),
        new DemoProduto(2, 1, "Água Mineral 500ml", 0.90m, 2.50m),
        new DemoProduto(3, 1, "Suco de Laranja 1L", 3.20m, 6.90m),
        new DemoProduto(4, 2, "Arroz 5kg", 18.00m, 27.90m),
        new DemoProduto(5, 2, "Feijão 1kg", 6.50m, 9.90m),
        new DemoProduto(6, 2, "Macarrão 500g", 2.80m, 4.90m),
        new DemoProduto(7, 2, "Óleo de Soja 900ml", 5.20m, 7.90m),
        new DemoProduto(8, 3, "Detergente 500ml", 1.80m, 3.50m),
        new DemoProduto(9, 3, "Sabão em Pó 1kg", 9.00m, 14.90m),
        new DemoProduto(10, 3, "Água Sanitária 2L", 4.00m, 6.50m),
        new DemoProduto(11, 4, "Pão de Forma", 5.50m, 8.90m),
        new DemoProduto(12, 4, "Bolo Caseiro", 12.00m, 19.90m),
    };

    /// <summary>
    /// Todos os Ids do manifesto para um tenant. O "limpar" só apaga linhas cujo
    /// Id esteja neste conjunto (e que não tenham referência viva).
    /// </summary>
    public static HashSet<Guid> TodosOsIds(Guid empresaId)
    {
        var ids = new HashSet<Guid>();
        foreach (var c in Categorias) ids.Add(CategoriaId(empresaId, c.Slot));
        foreach (var p in Produtos)
        {
            ids.Add(ProdutoId(empresaId, p.Slot));
            ids.Add(ItemEstoqueId(empresaId, p.Slot));
            ids.Add(EntradaId(empresaId, p.Slot));
            ids.Add(VendaId(empresaId, p.Slot));
        }
        return ids;
    }
}
