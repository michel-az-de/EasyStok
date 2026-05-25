using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;

namespace EasyStock.Application.UseCases.Fiscal.ConsultarNfe;

/// <summary>
/// Query simples: carrega NfeDocumento com Itens + Eventos pelo Id.
/// Retorna null se nao encontrado (controller traduz pra 404).
/// </summary>
public class ConsultarNfeUseCase(INfeRepository nfeRepo) : IUseCase<ConsultarNfeQuery, ConsultarNfeResult?>
{
    public async Task<ConsultarNfeResult?> ExecuteAsync(ConsultarNfeQuery query)
    {
        UseCaseGuards.EnsureEmpresaId(query.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(query.NfeId, "NfeId");

        var nfe = await nfeRepo.GetByIdWithDetailsAsync(query.EmpresaId, query.NfeId);
        if (nfe is null) return null;

        return new ConsultarNfeResult(
            Id: nfe.Id,
            EmpresaId: nfe.EmpresaId,
            PedidoId: nfe.PedidoId,
            Modelo: nfe.Modelo,
            Serie: nfe.Serie,
            Numero: nfe.Numero,
            ChaveAcesso: nfe.ChaveAcesso,
            Status: nfe.Status,
            ProtocoloAutorizacao: nfe.ProtocoloAutorizacao,
            DataAutorizacao: nfe.DataAutorizacao,
            MotivoRejeicao: nfe.MotivoRejeicao,
            DanfeUrl: nfe.DanfeUrl,
            TotalNota: nfe.TotalNota.Valor,
            CriadoEm: nfe.CriadoEm,
            AlteradoEm: nfe.AlteradoEm,
            Itens: nfe.Itens
                .OrderBy(i => i.Ordem)
                .Select(i => new ConsultarNfeItemDto(
                    Id: i.Id,
                    Ordem: i.Ordem,
                    NomeSnapshot: i.NomeSnapshot,
                    Quantidade: i.Quantidade,
                    PrecoUnitario: i.PrecoUnitario.Valor,
                    Unidade: i.Unidade,
                    Ncm: i.NcmSnapshot,
                    Cfop: i.CfopSnapshot,
                    CstOuCsosn: i.CstOuCsosn))
                .ToList(),
            Eventos: nfe.Eventos
                .OrderBy(e => e.OcorridoEm)
                .Select(e => new ConsultarNfeEventoDto(
                    Id: e.Id,
                    Tipo: e.Tipo,
                    OcorridoEm: e.OcorridoEm,
                    UsuarioNome: e.UsuarioNome,
                    Origem: e.Origem,
                    DadosJson: e.DadosJson))
                .ToList());
    }
}
