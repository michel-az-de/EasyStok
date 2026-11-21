namespace EasyStock.Application.Ports.Output.Persistence.Atendimento;

/// <summary>Manutenção da origem de anúncio gravada em pedidos e conversas (#2928).</summary>
public interface IOrigemAnuncioRepository
{
    /// <summary>
    /// Anula <c>IdClique</c> e <c>TipoIdClique</c> de pedidos e conversas, de todas as empresas, capturados antes de
    /// <paramref name="capturadaAntesDe"/>; plataforma, campanha, UTMs e anúncio ficam. Idempotente. Devolve as linhas alteradas.
    /// </summary>
    Task<int> PodarIdsDeCliqueAsync(DateTimeOffset capturadaAntesDe, CancellationToken ct = default);
}
