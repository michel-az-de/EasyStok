namespace EasyStock.Web.Models.Api;

/// <summary>Estado do convite de primeiro acesso (N9): <c>pendente</c>, <c>aceito</c> (com a via já mascarada) ou <c>nenhum</c>.</summary>
public record ConviteDoUsuario(string Estado = "nenhum", DateTime? AceitoEm = null, string? Via = null);

public record Usuario(
    Guid UsuarioId, string Nome, string Email, bool Ativo, DateTime? UltimoAcessoEm, DateTime CriadoEm, string Nivel = "Operador",
    ConviteDoUsuario? Convite = null);
