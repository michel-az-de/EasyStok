namespace EasyStock.Domain.ValueObjects;

/// <summary>
/// Dados fiscais opcionais — preparados para emissao de NFS-e futura. Persistido
/// como jsonb em <c>faturas.dados_fiscais</c>.
///
/// <para>
/// Hoje sao apenas snapshot informativo. Quando a integracao fiscal real for
/// habilitada (adapter SEFAZ/NotaJa/NFE.io), esses campos serao consumidos pelo
/// <c>INfSeProvider.EmitirAsync</c>. Schema versao 1.
/// </para>
/// </summary>
public sealed record DadosFiscais(
    string? CodigoServico = null,
    decimal? AliquotaIss = null,
    bool IssRetido = false,
    string? NaturezaOperacao = null,
    decimal? RetencaoIr = null,
    decimal? RetencaoCsll = null,
    decimal? RetencaoPis = null,
    decimal? RetencaoCofins = null,
    decimal? RetencaoInss = null,
    string? DescricaoServico = null,
    int SchemaVersao = DadosFiscais.SchemaVersaoAtual
)
{
    public const int SchemaVersaoAtual = 1;
}
