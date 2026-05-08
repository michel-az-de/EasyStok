using EasyStock.Domain.Enums;

namespace EasyStock.Application.UseCases.TicketSuporte
{
    public sealed record AbrirTicketRequest(
        string Titulo,
        string Descricao,
        TicketCategoria Categoria,
        /// <summary>F9 — FK opcional a uma Fatura que motivou o ticket.</summary>
        Guid? FaturaId = null,
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
