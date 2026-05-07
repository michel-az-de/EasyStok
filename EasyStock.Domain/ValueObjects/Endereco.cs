namespace EasyStock.Domain.ValueObjects;

/// <summary>
/// Endereco basico usado em snapshots de <see cref="DadosFaturado"/> e
/// <see cref="DadosEmissor"/>. Persistido como sub-objeto JSON dentro do
/// jsonb da fatura — nao tem tabela propria.
/// </summary>
public sealed record Endereco(
    string? Logradouro = null,
    string? Numero = null,
    string? Complemento = null,
    string? Bairro = null,
    string? Cidade = null,
    string? Uf = null,
    string? Cep = null,
    string? Pais = "BR"
);
