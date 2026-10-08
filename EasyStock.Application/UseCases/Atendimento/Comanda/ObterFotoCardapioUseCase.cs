using EasyStock.Application.Ports.Output.Storage;

namespace EasyStock.Application.UseCases.Atendimento.Comanda;

public sealed record FotoCardapioResult(byte[] Conteudo, string Mime);

/// <summary>
/// Foto do cardápio servida pela API por chave (#1448). As URLs gravadas no banco carregam o host da
/// época do upload (ex.: <c>ez-api.92.113.33.60.sslip.io/files/...</c>) e o console em produção só
/// alcança a API pelo <c>/api</c> da própria origem. O console troca <c>.../files/cardapios/X</c> por
/// <c>api/public/cardapio/fotos/X</c> e a foto sai daqui, de qualquer host gravado, sem reescrever dado.
///
/// <para>
/// Só lê o prefixo <see cref="Prefixo"/>, que já é público (é a foto que a vitrine mostra). Mídia do
/// atendimento e qualquer outra pasta do storage ficam de fora; caminho com <c>..</c>, segmento vazio,
/// barra invertida ou extensão que não é foto devolve nulo (404 na borda).
/// </para>
/// </summary>
public sealed class ObterFotoCardapioUseCase(IFileStorage fileStorage)
{
    public const string Prefixo = "cardapios/";
    public const int CaminhoTamanhoMaximo = 400;

    private static readonly Dictionary<string, string> MimePorExtensao = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".webp"] = "image/webp",
        [".gif"] = "image/gif",
    };

    public async Task<FotoCardapioResult?> ExecuteAsync(string? caminho, CancellationToken ct = default)
    {
        var chave = ChaveDoCaminho(caminho);
        if (chave is null || !await fileStorage.ExistsAsync(chave, ct)) return null;

        var conteudo = await fileStorage.DownloadAsync(chave, ct);
        return new FotoCardapioResult(conteudo, MimePorExtensao[Path.GetExtension(chave)]);
    }

    /// <summary>Chave do storage para o caminho depois de <c>cardapios/</c>, ou nulo se não for foto do cardápio.</summary>
    public static string? ChaveDoCaminho(string? caminho)
    {
        if (string.IsNullOrWhiteSpace(caminho) || caminho.Length > CaminhoTamanhoMaximo) return null;
        if (caminho.IndexOfAny(['\\', ':', '\0', '%']) >= 0) return null;

        var segmentos = caminho.Split('/');
        if (segmentos.Any(s => s.Length == 0 || s == "." || s == "..")) return null;
        if (!MimePorExtensao.ContainsKey(Path.GetExtension(segmentos[^1]))) return null;

        return Prefixo + caminho;
    }
}
