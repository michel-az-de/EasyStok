using EasyStock.Domain.Enums.Fiscal;

namespace EasyStock.Domain.Exceptions.Fiscal;

/// <summary>
/// Lançada quando o agregado <see cref="Entities.Fiscal.NotaFiscal"/> tenta
/// uma transição não permitida pela
/// <see cref="Sales.NotaFiscalStateMachine"/>.
/// Mapeada pela camada Application em 409 (conflito de estado).
/// </summary>
public sealed class TransicaoNotaFiscalInvalidaException : Exception
{
    public StatusNotaFiscal De { get; }
    public StatusNotaFiscal Para { get; }

    public TransicaoNotaFiscalInvalidaException(StatusNotaFiscal de, StatusNotaFiscal para)
        : base($"Transição inválida em NotaFiscal: {de} → {para}.")
    {
        De = de;
        Para = para;
    }

    public TransicaoNotaFiscalInvalidaException(string mensagem) : base(mensagem)
    {
    }
}
