namespace EasyStock.Domain.ValueObjects;

/// <summary>
/// Snapshot dos dados do destinatario (faturado) no momento da emissao da fatura.
/// Persistido como jsonb em <c>faturas.dados_faturado</c>. Preserva o estado
/// historico mesmo se o <see cref="Entities.Cliente"/> for editado depois.
///
/// <para>
/// <see cref="SchemaVersao"/> permite evolucao backward-compatible: ao adicionar
/// campos novos, incremente a versao e o renderer/migrator pode tratar versoes
/// antigas. Versao atual: 1.
/// </para>
/// </summary>
public sealed record DadosFaturado(
    string Nome,
    string? Documento = null,
    string? Email = null,
    string? Telefone = null,
    Endereco? Endereco = null,
    int SchemaVersao = DadosFaturado.SchemaVersaoAtual
)
{
    public const int SchemaVersaoAtual = 1;
}
