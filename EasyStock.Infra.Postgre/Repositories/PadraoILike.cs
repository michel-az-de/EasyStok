namespace EasyStock.Infra.Postgre.Repositories;

/// <summary>
/// Padrão "contém" para <c>EF.Functions.ILike</c> com os curingas do termo escapados (#2095, F15): sem isto a busca
/// "%" trazia tudo e "_" casava qualquer caractere. Use com <see cref="Escape"/> como caractere de escape.
/// </summary>
internal static class PadraoILike
{
    public const string Escape = "\\";

    public static string Contendo(string termo) =>
        "%" + termo.Trim().Replace(Escape, Escape + Escape).Replace("%", Escape + "%").Replace("_", Escape + "_") + "%";
}
