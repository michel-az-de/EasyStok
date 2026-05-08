using EasyStock.Domain.Enums.Fiscal;

namespace EasyStock.Domain.Entities.Fiscal;

/// <summary>
/// Contador de numeração fiscal por (empresa, loja, modelo, série).
/// Usado pelo NumeracaoNotaFiscalService com SELECT FOR UPDATE para
/// reservar próximo número de forma atômica (ADR-004).
/// </summary>
public sealed class NotaFiscalContador
{
    public Guid EmpresaId { get; set; }
    public Guid LojaId { get; set; }
    public ModeloDocumentoFiscal Modelo { get; set; }
    public int Serie { get; set; }
    public int UltimoNumero { get; set; }
    public DateTime AtualizadoEm { get; set; }
}
