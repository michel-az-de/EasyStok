using EasyStock.Domain.Entities.Atendimento;

namespace EasyStock.Application.Ports.Output.Persistence.Atendimento;

/// <summary>Links do cardápio ligados à conversa (S48). O token nunca é gravado: a busca é pelo hash.</summary>
public interface ILinkCardapioConversaRepository
{
    Task AddAsync(LinkCardapioConversa link, CancellationToken ct = default);

    /// <summary>
    /// Busca pelo hash em todas as empresas (bypass de RLS): a requisição do site é anônima e a URL não
    /// diz de qual loja é. O hash de 256 bits é a credencial; quem chama liga o tenant do link encontrado.
    /// </summary>
    Task<LinkCardapioConversa?> ObterPorTokenHashAsync(string tokenHash, CancellationToken ct = default);

    /// <summary>
    /// Marca o uso de forma atômica (<c>UsadoEm IS NULL AND ExpiraEm &gt; agora</c>). Devolve
    /// <c>false</c> se outra requisição usou antes ou se venceu: dois envios simultâneos não criam dois pedidos.
    /// </summary>
    Task<bool> TentarConsumirAsync(Guid empresaId, Guid linkId, DateTime agora, CancellationToken ct = default);

    /// <summary>Desfaz o uso quando o pedido foi recusado, para o cliente corrigir o carrinho e reenviar.</summary>
    Task LiberarAsync(Guid empresaId, Guid linkId, CancellationToken ct = default);
}
