namespace EasyStock.Domain.Entities.Notifications;

/// <summary>
/// Último estado conhecido de um template da Meta na WABA (N6): a categoria que a Meta cobra hoje. Tabela global, sem
/// <c>EmpresaId</c>, porque a WABA é uma só. Quando a Meta recategoriza um template para marketing
/// (<c>template_category_update</c>) ela o mantém <c>APPROVED</c> e passa a cobrar como marketing; é este registro que
/// impede o envio pela plataforma. A chave é <c>(Nome, Idioma)</c> normalizados (<see cref="NormalizarIdioma"/>).
/// </summary>
public class TemplateMetaEstado
{
    public const string CategoriaMarketing = "MARKETING";

    public string Nome { get; private set; } = null!;
    public string Idioma { get; private set; } = null!;

    /// <summary>Categoria da Meta em maiúsculas (<c>MARKETING</c>, <c>UTILITY</c>, <c>AUTHENTICATION</c>).</summary>
    public string CategoriaAtual { get; private set; } = null!;

    public DateTime AtualizadoEm { get; private set; }

    public static TemplateMetaEstado Criar(string nome, string idioma, string categoria) => new()
    {
        Nome = NormalizarNome(nome),
        Idioma = NormalizarIdioma(idioma),
        CategoriaAtual = NormalizarCategoria(categoria),
        AtualizadoEm = DateTime.UtcNow
    };

    public void AtualizarCategoria(string categoria)
    {
        CategoriaAtual = NormalizarCategoria(categoria);
        AtualizadoEm = DateTime.UtcNow;
    }

    public bool EhMarketing => CategoriaAtual == CategoriaMarketing;

    public static string NormalizarNome(string nome) => nome.Trim().ToLowerInvariant();

    /// <summary>
    /// O webhook manda o idioma com hífen (<c>pt-BR</c>) e o envio usa sublinhado (<c>pt_BR</c>): os dois casam
    /// depois de normalizados (sublinhado e minúsculas).
    /// </summary>
    public static string NormalizarIdioma(string idioma) =>
        idioma.Trim().Replace('-', '_').ToLowerInvariant();

    public static string NormalizarCategoria(string categoria) => categoria.Trim().ToUpperInvariant();
}
