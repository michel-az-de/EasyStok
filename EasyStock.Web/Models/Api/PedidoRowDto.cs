namespace EasyStock.Web.Models.Api;

/// <summary>
/// Projeção enxuta de <see cref="Pedido"/> para as respostas JSON do cockpit (issue #591).
/// Contém só o que a linha + KPIs precisam re-renderizar após uma ação AJAX, e é a MESMA
/// projeção usada para semear o store no SSR (anti-flash de hidratação).
/// O Web é BFF HTTP puro (sem ProjectReference a Domain) → este DTO nunca expõe entidade
/// de domínio; espelha o record <see cref="Pedido"/> da camada Web.
/// </summary>
public sealed record PedidoRowDto(
    string Id,
    string Status,
    string? ClienteNome,
    string? ClienteApt,
    string? ClienteTelefone,
    decimal Total,
    decimal TotalPago,
    decimal Pendente,
    bool Quitado,
    int ItensCount,
    DateTime CriadoEm,
    DateTime? AgendadoParaEm,
    bool IsScheduled,
    bool IsAtrasado)
{
    public static PedidoRowDto From(Pedido p) => new(
        p.Id, p.Status, p.ClienteNome, p.ClienteApt, p.ClienteTelefone,
        p.Total, p.TotalPago, p.Pendente, p.Quitado, p.ItensCount,
        p.CriadoEm, p.AgendadoParaEm, p.IsScheduled, p.IsAtrasado);
}
