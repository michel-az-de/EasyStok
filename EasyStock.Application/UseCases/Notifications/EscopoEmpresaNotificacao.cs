using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Entities.Notifications;

namespace EasyStock.Application.UseCases.Notifications;

/// <summary>
/// Template, rotina ou bloqueio que não existe ou não é da empresa do comando. Recurso de outra
/// empresa e recurso global (<c>EmpresaId</c> nulo) caem aqui do mesmo jeito, para o tenant não
/// descobrir o que existe fora dele (P01-B, #1173).
/// </summary>
public sealed class ConfiguracaoNotificacaoNaoEncontradaException(string oQue, Guid id)
    : Exception($"{oQue} {id} não encontrado.");

/// <summary>Leitura por id com a empresa no filtro: o dono é conferido antes de qualquer mudança.</summary>
internal static class EscopoEmpresaNotificacao
{
    public static async Task<TemplateNotificacao> ObterDaEmpresaAsync(
        this ITemplateRepository repository, Guid templateId, Guid empresaId)
    {
        var template = await repository.GetByIdAsync(templateId);
        return template is not null && template.EmpresaId == empresaId
            ? template
            : throw new ConfiguracaoNotificacaoNaoEncontradaException("Template", templateId);
    }

    public static async Task<RotinaNotificacao> ObterDaEmpresaAsync(
        this IRotinaRepository repository, Guid rotinaId, Guid empresaId)
    {
        var rotina = await repository.GetByIdAsync(rotinaId);
        return rotina is not null && rotina.EmpresaId == empresaId
            ? rotina
            : throw new ConfiguracaoNotificacaoNaoEncontradaException("Rotina", rotinaId);
    }
}
