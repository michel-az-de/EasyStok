namespace EasyStock.Web.Models.Api;

public record Loja(string Id, string Nome, string? Emoji, string Cidade, string Plano, string? EmpresaId = null);
