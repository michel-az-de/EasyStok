using System.Text.Json;

namespace EasyStock.Application.UseCases.GerenciarProduto.Helpers;

/// <summary>
/// Serializa/desserializa o JSON de fotos armazenado em <c>Produto.FotosJson</c>.
///
/// Extraido dos helpers privados do facade <c>GerenciarProdutoUseCase</c> (F9)
/// pra ser compartilhado entre os comandos especializados (ReordenarFotos,
/// GerenciarUploads) e queries (ObterDetalhe).
///
/// Falha de desserializacao retorna lista vazia — defesa em profundidade pra
/// produtos com JSON corrompido por dados legados.
/// </summary>
internal static class ProdutoFotosSerializer
{
    public static IReadOnlyCollection<ProdutoFotoMetadata> Deserializar(string? fotosJson)
    {
        if (string.IsNullOrWhiteSpace(fotosJson))
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<ProdutoFotoMetadata>>(fotosJson) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static string Serializar(IEnumerable<ProdutoFotoMetadata> fotos) =>
        JsonSerializer.Serialize(fotos);
}
