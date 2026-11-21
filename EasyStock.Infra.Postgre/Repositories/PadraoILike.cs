namespace EasyStock.Infra.Postgre.Repositories;

/// <summary>
/// Padrões para <c>EF.Functions.ILike</c> com os curingas do termo escapados (#2095, F15): sem isto a busca
/// "%" trazia tudo e "_" casava qualquer caractere. Use com <see cref="Escape"/> como caractere de escape.
/// </summary>
public static class PadraoILike
{
    public const string Escape = "\\";

    public static string Contendo(string termo) => "%" + Literal(termo.Trim()) + "%";

    /// <summary>
    /// Igualdade sem diferenciar maiúsculas (#2477): o termo inteiro vale como texto, nunca como curinga.
    /// </summary>
    public static string Literal(string termo) =>
        termo.Replace(Escape, Escape + Escape).Replace("%", Escape + "%").Replace("_", Escape + "_");
}
