namespace EasyStock.Application.Ports.Output.Fiscal;

/// <summary>
/// Resolve a implementacao de <see cref="IGatewayFiscal"/> correta para o tenant
/// com base no <see cref="ConfigFiscalDto.Provedor"/>. Permite coexistir
/// adapters reais (Focus NFe, eNotas) e mock em um mesmo deployment — cada
/// empresa escolhe via <c>EmpresaConfiguracaoFiscal.ProvedorPreferido</c>.
/// </summary>
public interface IGatewayFiscalFactory
{
    /// <summary>
    /// Retorna o gateway cuja <see cref="IGatewayFiscal.Provedor"/> bate com o
    /// nome solicitado (case-insensitive). Lanca se nao houver adapter registrado.
    /// </summary>
    /// <exception cref="EasyStock.Domain.Exceptions.RegraDeDominioVioladaException">
    /// Provedor desconhecido ou nao registrado no container DI.
    /// </exception>
    IGatewayFiscal ObterPara(string provedor);
}
