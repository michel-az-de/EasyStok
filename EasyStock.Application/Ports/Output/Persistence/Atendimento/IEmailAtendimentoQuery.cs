namespace EasyStock.Application.Ports.Output.Persistence.Atendimento;

/// <summary>Consultas da entrada de e-mail do atendimento (#1432).</summary>
public interface IEmailAtendimentoQuery
{
    /// <summary>
    /// Empresas com caixa de suporte ativa (credencial <c>caixa-email</c>). Atravessa tenants: exige bypass de RLS,
    /// como as outras varreduras dos jobs.
    /// </summary>
    Task<IReadOnlyList<Guid>> ListarEmpresasComCaixaAsync(CancellationToken ct = default);

    /// <summary>Cliente ativo da empresa com este e-mail (sem diferenciar caixa), o mais antigo se houver mais de um.</summary>
    Task<Guid?> ObterClienteIdPorEmailAsync(Guid empresaId, string email, CancellationToken ct = default);
}
