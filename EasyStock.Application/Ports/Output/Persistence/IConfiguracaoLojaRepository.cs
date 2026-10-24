namespace EasyStock.Application.Ports.Output.Persistence;

public interface IConfiguracaoLojaRepository
{
    Task<ConfiguracaoLoja?> GetByLojaIdAsync(Guid lojaId);
    Task<ConfiguracaoLoja> GetOrDefaultAsync(Guid lojaId);

    /// <summary>
    /// A configuração da loja do movimento. Sem <paramref name="lojaId"/> (pedido do site, da comanda e do
    /// console nascem sem loja, #1534), a da loja única ATIVA da empresa (a desativada não conta); com mais de uma loja ativa e sem indicação,
    /// nulo, e quem chama usa o padrão. Ignora o filtro de tenant: serve também ao sync do aparelho (sem JWT);
    /// o isolamento é o <paramref name="empresaId"/>.
    /// </summary>
    Task<ConfiguracaoLoja?> GetDaLojaAsync(Guid empresaId, Guid? lojaId, CancellationToken ct = default);
    Task AddAsync(ConfiguracaoLoja configuracao);
    Task UpdateAsync(ConfiguracaoLoja configuracao);
}
