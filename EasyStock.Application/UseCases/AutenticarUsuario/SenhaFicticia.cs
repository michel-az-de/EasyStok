namespace EasyStock.Application.UseCases.AutenticarUsuario;

/// <summary>
/// #1781: hash conferido quando não há senha real a conferir (conta inexistente, inativa ou bloqueada). Sem ele o
/// login responde em ~10 ms para e-mail desconhecido e em ~150 ms para conta existente, e o tempo enumera contas.
/// </summary>
public static class SenhaFicticia
{
    /// <summary>
    /// BCrypt de custo 11 (o mesmo do <c>BCryptPasswordHasher</c>) de uma senha aleatória descartada na geração:
    /// nenhuma senha confere com ele.
    /// </summary>
    public const string Hash = "$2a$11$H0PJ/KEN7i/IXk.2Y.iMMu5ySZEKG0F1PqKgp.cmeeSv5TTzVmSL6";
}
