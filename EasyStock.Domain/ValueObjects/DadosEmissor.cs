namespace EasyStock.Domain.ValueObjects;

/// <summary>
/// Snapshot dos dados do emissor (empresa) no momento da emissao da fatura.
/// Persistido como jsonb em <c>faturas.dados_emissor</c>. Preserva razao social,
/// CNPJ e endereco como estavam quando a fatura foi emitida.
///
/// <para>
/// <see cref="SchemaVersao"/> permite evolucao. Versao atual: 1. Quando NFS-e
/// fiscal for habilitada, novos campos (regime tributario, IM, IE) podem ser
/// adicionados sem quebrar faturas antigas.
/// </para>
/// </summary>
public sealed record DadosEmissor(
    string Nome,
    string? Documento = null,
    string? RazaoSocial = null,
    string? InscricaoMunicipal = null,
    string? InscricaoEstadual = null,
    string? RegimeTributario = null,
    Endereco? Endereco = null,
    string? Email = null,
    string? Telefone = null,
    int SchemaVersao = DadosEmissor.SchemaVersaoAtual
)
{
    public const int SchemaVersaoAtual = 1;
}
