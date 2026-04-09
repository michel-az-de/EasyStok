namespace EasyStock.Web.Models.Api;

public record Usuario(Guid UsuarioId, string Nome, string Email, bool Ativo, DateTime? UltimoAcessoEm, DateTime CriadoEm, string Nivel = "Operador");
