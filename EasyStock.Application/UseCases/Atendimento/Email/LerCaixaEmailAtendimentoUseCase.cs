using EasyStock.Application.Ports.Output.Atendimento.Email;
using EasyStock.Application.Ports.Output.Integration.Crypto;
using EasyStock.Application.UseCases.FeatureFlags;
using EasyStock.Domain.Integration;

namespace EasyStock.Application.UseCases.Atendimento.Email;

/// <summary>Contagem de uma rodada na caixa de uma empresa.</summary>
public sealed record LeituraCaixaEmailResultado(int Gravados, int Duplicados, int Ignorados, int Falhas)
{
    public static readonly LeituraCaixaEmailResultado Nada = new(0, 0, 0, 0);
}

/// <summary>
/// Uma rodada na caixa de suporte de uma empresa (#1432): abre a INBOX, lê até <c>maximo</c> não lidos, grava
/// cada um (<see cref="ReceberEmailAtendimentoUseCase"/>) e só então marca como lido. O que falhou continua não
/// lido para a próxima rodada; gravado, repetido e ignorado são marcados. Sem módulo de atendimento ou sem
/// caixa configurada, não faz nada. O escopo é de uma empresa só: o tenant é fixado aqui (RLS, ADR-0010).
/// </summary>
public sealed class LerCaixaEmailAtendimentoUseCase(
    IIntegrationCredentialResolver credenciais,
    ITenantFeatureFlagRepository featureFlagRepository,
    ICaixaEmailCliente cliente,
    ReceberEmailAtendimentoUseCase receber,
    ITenantContextAccessor tenantContext,
    ILogger<LerCaixaEmailAtendimentoUseCase> logger)
{
    public async Task<LeituraCaixaEmailResultado> ExecuteAsync(Guid empresaId, int maximo, CancellationToken ct = default)
    {
        tenantContext.SetCurrentTenant(empresaId);

        var flags = await featureFlagRepository.ListarAtivasAsync(empresaId, ct);
        if (!flags.Contains(FeatureCatalogo.ModuloAtendimento, StringComparer.OrdinalIgnoreCase))
            return LeituraCaixaEmailResultado.Nada;

        var caixa = await credenciais.ObterAsync<CaixaEmailAtendimento>(
            empresaId, CaixaEmailAtendimento.ProviderKey, AmbienteIntegracao.Production, ct);
        if (caixa is null)
            return LeituraCaixaEmailResultado.Nada;

        int gravados = 0, duplicados = 0, ignorados = 0, falhas = 0;
        await using var sessao = await cliente.AbrirAsync(caixa, ct);
        foreach (var id in await sessao.ListarNaoLidosAsync(maximo, ct))
        {
            try
            {
                var email = await sessao.BaixarAsync(id, ct);
                var desfecho = await receber.ExecuteAsync(empresaId, caixa, email, ct);
                await sessao.MarcarLidoAsync(id, ct);
                switch (desfecho)
                {
                    case DesfechoEmailRecebido.Gravado: gravados++; break;
                    case DesfechoEmailRecebido.Duplicado: duplicados++; break;
                    default: ignorados++; break;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                // Sem endereço no log (LGPD): o id do IMAP basta para achar o e-mail na caixa.
                falhas++;
                logger.LogWarning(ex, "Caixa de e-mail da empresa {EmpresaId}: e-mail {IdNaCaixa} não entrou; fica não lido.", empresaId, id);
            }
        }

        return new LeituraCaixaEmailResultado(gravados, duplicados, ignorados, falhas);
    }
}
