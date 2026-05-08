using EasyStock.Domain.Enums.Fiscal;
using EasyStock.Domain.Exceptions.Fiscal;

namespace EasyStock.Domain.Sales;

/// <summary>
/// Máquina de estados do agregado NotaFiscal (NFC-e e NFe). Centraliza
/// transições válidas e estados terminais. Use
/// <see cref="EnsureTransicaoValida"/> antes de mudar status no agregado.
/// Idempotência (de == para) é tratada como no-op silencioso pelo caller.
/// </summary>
public static class NotaFiscalStateMachine
{
    public static IReadOnlyDictionary<StatusNotaFiscal, IReadOnlySet<StatusNotaFiscal>> Transicoes { get; } =
        new Dictionary<StatusNotaFiscal, IReadOnlySet<StatusNotaFiscal>>
        {
            [StatusNotaFiscal.EmEmissao] = new HashSet<StatusNotaFiscal>
            {
                StatusNotaFiscal.Autorizada,
                StatusNotaFiscal.Rejeitada,
                StatusNotaFiscal.Denegada,
                StatusNotaFiscal.EmContingencia,
            },
            [StatusNotaFiscal.EmContingencia] = new HashSet<StatusNotaFiscal>
            {
                StatusNotaFiscal.Autorizada,
                StatusNotaFiscal.Rejeitada,
            },
            [StatusNotaFiscal.Autorizada] = new HashSet<StatusNotaFiscal>
            {
                StatusNotaFiscal.CancelamentoEmAndamento,
                StatusNotaFiscal.Denegada,
            },
            [StatusNotaFiscal.CancelamentoEmAndamento] = new HashSet<StatusNotaFiscal>
            {
                StatusNotaFiscal.Cancelada,
                StatusNotaFiscal.Autorizada,
            },
            [StatusNotaFiscal.Rejeitada] = new HashSet<StatusNotaFiscal>(),
            [StatusNotaFiscal.Denegada] = new HashSet<StatusNotaFiscal>(),
            [StatusNotaFiscal.Cancelada] = new HashSet<StatusNotaFiscal>(),
            [StatusNotaFiscal.Inutilizada] = new HashSet<StatusNotaFiscal>(),
        };

    public static IReadOnlySet<StatusNotaFiscal> Finais { get; } = new HashSet<StatusNotaFiscal>
    {
        StatusNotaFiscal.Cancelada,
        StatusNotaFiscal.Rejeitada,
        StatusNotaFiscal.Denegada,
        StatusNotaFiscal.Inutilizada,
    };

    public static bool TransicaoValida(StatusNotaFiscal de, StatusNotaFiscal para)
        => Transicoes.TryGetValue(de, out var destinos) && destinos.Contains(para);

    public static void EnsureTransicaoValida(StatusNotaFiscal de, StatusNotaFiscal para)
    {
        if (de == para) return;
        if (!TransicaoValida(de, para))
            throw new TransicaoNotaFiscalInvalidaException(de, para);
    }

    public static bool EhTerminal(StatusNotaFiscal status) => Finais.Contains(status);
}
