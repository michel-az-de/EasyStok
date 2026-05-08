using EasyStock.Domain.Entities.Fiscal;
using EasyStock.Domain.Enums.Fiscal;

namespace EasyStock.Application.UseCases.Fiscal.EmitirNotaFiscalConsumidor;

public sealed record EmitirNotaFiscalConsumidorResult(
    Guid NotaFiscalId,
    string ChaveAcesso,
    string ChaveAcessoFormatada,
    StatusNotaFiscal Status,
    int Numero,
    int Serie,
    short Modelo,
    string? Protocolo,
    DateTime? DhAutorizacao,
    string? UrlConsultaQr,
    string? MotivoRejeicao,
    string? CodigoRejeicao,
    bool EmContingencia,
    decimal ValorTotal)
{
    public static EmitirNotaFiscalConsumidorResult From(NotaFiscal nota) =>
        new(
            NotaFiscalId: nota.Id,
            ChaveAcesso: nota.ChaveAcesso.Valor,
            ChaveAcessoFormatada: nota.ChaveAcesso.Formatada,
            Status: nota.Status,
            Numero: nota.Numero,
            Serie: nota.Serie,
            Modelo: (short)nota.Modelo,
            Protocolo: nota.ProtocoloAutorizacao,
            DhAutorizacao: nota.DataAutorizacao,
            UrlConsultaQr: null,
            MotivoRejeicao: nota.MotivoRejeicao,
            CodigoRejeicao: nota.CodigoRejeicao,
            EmContingencia: nota.Status == StatusNotaFiscal.EmContingencia,
            ValorTotal: nota.ValorTotal.Valor);
}
