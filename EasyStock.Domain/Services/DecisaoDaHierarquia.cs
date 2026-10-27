namespace EasyStock.Domain.Services;

/// <summary>Códigos de recusa da política de hierarquia (#2070, ADR-0038). Saem iguais no corpo do erro da API.</summary>
public static class CodigosDaHierarquia
{
    public const string Hierarquia = "HIERARQUIA";
    public const string UsuarioProprio = "USUARIO_PROPRIO";
    public const string DonaProtegida = "DONA_PROTEGIDA";
    public const string UltimaDona = "ULTIMA_DONA";
    public const string DonaNaoAdmin = "DONA_NAO_ADMIN";
    public const string DonaContaNaoProvada = "DONA_CONTA_NAO_PROVADA";
    public const string DonaSuporte = "DONA_SUPORTE";
    public const string DonaEmailExistente = "DONA_EMAIL_EXISTENTE";
}

/// <summary>
/// Resposta da <see cref="PoliticaDeHierarquia"/>: permitido, ou negado com um dos <see cref="CodigosDaHierarquia"/>.
/// <paramref name="ExigeMotivo"/>: o suporte trocando o e-mail da dona ou tirando a marca precisa justificar (10+ caracteres).
/// </summary>
public sealed record DecisaoDaHierarquia(bool Permitido, string? Codigo = null, bool ExigeMotivo = false)
{
    public static DecisaoDaHierarquia Permite { get; } = new(true);

    public static DecisaoDaHierarquia PermiteComMotivo { get; } = new(true, ExigeMotivo: true);

    public static DecisaoDaHierarquia Nega(string codigo) => new(false, codigo);
}
