using EasyStock.Domain.Enums.Campanhas;

namespace EasyStock.Application.Ports.Output.Persistence.Campanhas;

/// <summary>
/// Fatos de um cliente ativo que o cálculo do público (S29) precisa. A query só lê; filtrar e
/// excluir é do <c>CalcularPublicoCampanhaUseCase</c>.
/// </summary>
/// <param name="ConsentiuMarketing">
/// Marketing no WhatsApp: a linha de <c>ConsentimentoContato</c> (S38) quando existe; sem linha, o
/// booleano legado <c>Cliente.ConsentiuMarketing</c>.
/// </param>
/// <param name="UltimaCompraItemEm">Entrega mais recente de pedido com o item do filtro; null sem item no filtro ou sem compra.</param>
/// <param name="UltimaCampanhaRecebidaEm">
/// <c>EnviadoEm</c> mais recente de outra campanha (destinatário <c>Enviado</c> ou <c>Pediu</c>).
/// </param>
/// <param name="Telefone">Como está no cadastro; o disparo (S30) normaliza para E.164.</param>
public sealed record CandidatoPublicoCampanha(
    Guid ClienteId,
    string Nome,
    bool TemTelefone,
    bool Bloqueado,
    bool ConsentiuMarketing,
    IReadOnlyList<string> Tags,
    DateTime? UltimaCompraItemEm,
    DateTime? UltimaCampanhaRecebidaEm,
    string? Telefone = null);

/// <summary>Destinatário da campanha com o nome do cliente, para a dona conferir a lista.</summary>
public sealed record DestinatarioCampanhaResumo(
    Guid ClienteId,
    string Nome,
    StatusCampanhaDestinatario Status,
    string? MotivoExclusao,
    int Onda,
    DateTime? EnviadoEm);

/// <summary>Leituras do público da campanha (S29). <c>EmpresaId</c> no WHERE além do RLS (ADR-0010).</summary>
public interface ICampanhaPublicoQueries
{
    /// <summary>
    /// Clientes ativos da empresa. Com <paramref name="comprouItemId"/>, cada um traz a última entrega
    /// de pedido com esse item (<c>CardapioItemId</c> ou <c>ProdutoId</c>).
    /// </summary>
    Task<IReadOnlyList<CandidatoPublicoCampanha>> ListarCandidatosAsync(
        Guid empresaId, Guid campanhaId, Guid? comprouItemId, CancellationToken ct = default);

    /// <summary>Destinatários da campanha ordenados pelo nome, filtrados pelo status quando informado.</summary>
    Task<IReadOnlyList<DestinatarioCampanhaResumo>> ListarDestinatariosAsync(
        Guid empresaId, Guid campanhaId, StatusCampanhaDestinatario? status, int limite, CancellationToken ct = default);
}
