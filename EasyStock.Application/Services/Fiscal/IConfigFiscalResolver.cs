using EasyStock.Application.Ports.Output.Fiscal;

namespace EasyStock.Application.Services.Fiscal;

/// <summary>
/// Compoe um <see cref="ConfigFiscalDto"/> a partir da
/// <see cref="EasyStock.Domain.Fiscal.EmpresaConfiguracaoFiscal"/> + dados da empresa
/// + credencial decifrada do certificado A1. Implementacao em
/// <c>EasyStock.Infra.Postgre/Services/ConfigFiscalResolver.cs</c>.
///
/// <para>
/// Os use cases nao falam com Empresa/CredencialIntegracao diretamente — usam este
/// resolver para isolar a montagem do snapshot que vai ao <see cref="IGatewayFiscal"/>.
/// </para>
///
/// <para>
/// <b>Cache:</b> implementacao pode cachear por tenant com TTL curto (60s) para
/// evitar round-trips em alta frequencia. Invalida ao atualizar config fiscal
/// (event-driven via outbox).
/// </para>
/// </summary>
public interface IConfigFiscalResolver
{
    /// <summary>
    /// Resolve a configuracao fiscal completa do tenant, incluindo cert A1 decifrado.
    /// </summary>
    /// <exception cref="EasyStock.Domain.Exceptions.RegraDeDominioVioladaException">
    /// Tenant sem config fiscal, ou config nao habilitada, ou cert ausente em producao.
    /// </exception>
    Task<ConfigFiscalDto> ResolveAsync(Guid empresaId, CancellationToken ct = default);
}
