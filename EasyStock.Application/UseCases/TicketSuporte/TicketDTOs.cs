using EasyStock.Domain.Enums;

namespace EasyStock.Application.UseCases.TicketSuporte
{
    /// <summary>Request body de abertura de ticket pelo cliente.</summary>
    /// <param name="Titulo">Titulo curto do ticket.</param>
    /// <param name="Descricao">Descricao detalhada (vai como primeira mensagem).</param>
    /// <param name="Categoria">Categoria do helpdesk.</param>
    /// <param name="FaturaId">F9 — FK opcional a uma Fatura que motivou o ticket.</param>
    public sealed record AbrirTicketRequest(
        string Titulo,
        string Descricao,
        TicketCategoria Categoria,
        /// <summary>F9 — FK opcional a uma Fatura que motivou o ticket.</summary>
        Guid? FaturaId = null,
        /// <summary>Onda 1.1 — FK opcional a um Pedido que motivou o ticket.</summary>
        Guid? PedidoId = null,
        /// <summary>Canal de origem (Pwa, Web, Mobile, Admin, Site). Quando null, controller infere via User-Agent.</summary>
        CanalOrigem? CanalOrigem = null);

    public sealed record ResponderRequest(string Resposta);

    public sealed record TicketDetailDTO(
        Guid Id,
        string Titulo,
        string Status,
        string Categoria,
        string Prioridade,
        List<MensagemDTO> Mensagens,
        DateTime CriadoEm,
        DateTime AlteradoEm);

    public sealed record MensagemDTO(
        Guid Id,
        string Autor,
        string Conteudo,
        bool IsAdmin,
        DateTime CriadoEm);
}
