using System.Text.Json.Serialization;

namespace EasyStok.Mobile.Models;

// GET /api/lojas?empresaId={guid} — espelha LojaResult em
// EasyStock.Application.UseCases.Loja: (Id, EmpresaId, Nome, Ativa).
public sealed record LojaDto(
	[property: JsonPropertyName("id")] Guid Id,
	[property: JsonPropertyName("empresaId")] Guid EmpresaId,
	[property: JsonPropertyName("nome")] string Nome,
	[property: JsonPropertyName("ativa")] bool Ativa);
