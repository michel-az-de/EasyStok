namespace EasyStock.Web.Models.Api;

// Issue 810: modelo unico do endpoint GET lojas (antes Loja + LojaApi desserializavam
// a mesma resposta em shapes divergentes). Id/EmpresaId ficam string (alimentam
// SessionService.SetLoja no fluxo de auth); Ativa vem do LojaApi antigo.
public record Loja(string Id, string? EmpresaId, string Nome, string? Emoji, string? Cidade, string? Plano, bool Ativa);
