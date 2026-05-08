using System;
using EasyStock.Domain.Enums.Fiscal;

namespace EasyStock.Domain.Exceptions.Fiscal;

/// <summary>
/// Lançada quando a sequência fiscal de uma série/loja atinge o teto de
/// 999.999.999. Resolução operacional: trocar série (ADR-004).
/// </summary>
public sealed class NumeracaoEsgotadaException : Exception
{
    public Guid EmpresaId { get; }
    public Guid LojaId { get; }
    public ModeloDocumentoFiscal Modelo { get; }
    public int Serie { get; }

    public NumeracaoEsgotadaException(Guid empresaId, Guid lojaId, ModeloDocumentoFiscal modelo, int serie)
        : base($"Numeração esgotada para empresa {empresaId} loja {lojaId} modelo {(short)modelo} série {serie}. Trocar série.")
    {
        EmpresaId = empresaId;
        LojaId = lojaId;
        Modelo = modelo;
        Serie = serie;
    }
}
