namespace EasyStock.Application.UseCases.Financeiro.Common;

/// <summary>
/// #2095 (F6): já existe conta da empresa com o mesmo documento de referência. A API responde 409. Antes a criação
/// devolvia a conta antiga com 201 e a dívida nova (a NF 123 de outro fornecedor, uma conta manual) não era registrada.
/// A idempotência das integrações continua por <c>Origem + OrigemRefId</c>, verificada antes desta.
/// </summary>
public sealed class ContaFinanceiraDuplicadaException(string documentoReferencia, Guid contaExistenteId)
    : Exception($"Já existe conta com o documento {documentoReferencia}.")
{
    public string DocumentoReferencia { get; } = documentoReferencia;
    public Guid ContaExistenteId { get; } = contaExistenteId;
}
