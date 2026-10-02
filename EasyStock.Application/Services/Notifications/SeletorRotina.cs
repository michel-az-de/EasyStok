using EasyStock.Domain.Entities.Notifications;

namespace EasyStock.Application.Services.Notifications;

/// <summary>
/// Escolhe a rotina que vale para o evento (N5): a da empresa antes da global, na ordem recebida (o repositório
/// ordena por <c>CriadaEm</c>). Rotina sem canais não esconde a outra: o motor a trataria como "nada a fazer".
/// </summary>
public static class SeletorRotina
{
    public static RotinaNotificacao? Escolher(IEnumerable<RotinaNotificacao> candidatas, Guid empresaId)
    {
        var daEmpresa = candidatas.Where(r => r.EmpresaId == empresaId).ToList();
        var globais = candidatas.Where(r => r.EmpresaId is null).ToList();

        return daEmpresa.FirstOrDefault(TemCanais)
            ?? globais.FirstOrDefault(TemCanais)
            ?? daEmpresa.FirstOrDefault()
            ?? globais.FirstOrDefault();
    }

    private static bool TemCanais(RotinaNotificacao rotina) => CanaisDaRotina.Ler(rotina).Count > 0;
}
