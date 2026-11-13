namespace EasyStock.Application.UseCases.Etiquetas.Folha;

/// <summary>Todas as vagas de <see cref="VagasDeFolhaPdf"/> seguiram ocupadas durante a espera (#2200). A API responde 503.</summary>
public sealed class FolhaPdfOcupadaException()
    : InvalidOperationException("Outras folhas de etiqueta estão sendo geradas agora. Tente de novo em alguns segundos.")
{
    /// <summary>Valor do <c>Retry-After</c>.</summary>
    public const int TenteDeNovoEmSegundos = 5;
}
