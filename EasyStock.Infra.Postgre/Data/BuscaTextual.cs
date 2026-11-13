namespace EasyStock.Infra.Postgre.Data;

internal static class BuscaTextual
{
    public const string Acentos = "áàâãäéèêëíìîïóòôõöúùûüçñ";
    public const string SemAcentos = "aaaaaeeeeiiiiooooouuuucn";
    public const string MascaraTelefone = "()+-. ";

    public static string Normalizar(string texto)
    {
        var normalizado = texto.Trim().ToLowerInvariant();
        for (var i = 0; i < Acentos.Length; i++) normalizado = normalizado.Replace(Acentos[i], SemAcentos[i]);
        return normalizado;
    }

    public static string DigitosTelefone(string texto)
    {
        var digitos = new string(texto.Where(char.IsAsciiDigit).ToArray());
        return digitos.Length is 12 or 13 && digitos.StartsWith("55", StringComparison.Ordinal) ? digitos[2..] : digitos;
    }
}
