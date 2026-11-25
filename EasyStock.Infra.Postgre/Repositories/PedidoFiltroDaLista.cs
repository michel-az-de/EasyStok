using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories;

/// <summary>
/// Os filtros da lista de Pedidos de Cadastros, num lugar só (#3294): a lista paginada
/// (<see cref="PedidoRepository.GetByEmpresaAsync"/>) e o resumo (<see cref="PedidosResumoQuery"/>) recortam
/// os mesmos pedidos, então os números da faixa batem com as linhas. Quem chama já filtrou a empresa.
/// </summary>
public static class PedidoFiltroDaLista
{
    public static IQueryable<Pedido> Aplicar(
        IQueryable<Pedido> query, string? status, Guid? clienteId, DateTime? desde, DateTime? ate,
        string? search, DateTime? entregaDesde, DateTime? entregaAte)
    {
        // #2478: uma situação ou várias separadas por vírgula ("aguardando,aprovado_baba" é a "Na fila" da Cozinha).
        var situacoes = (status ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (situacoes.Length == 1) query = query.Where(p => p.Status == situacoes[0]);
        else if (situacoes.Length > 1) query = query.Where(p => situacoes.Contains(p.Status));
        if (clienteId.HasValue && clienteId.Value != Guid.Empty)
            query = query.Where(p => p.ClienteId == clienteId);
        if (desde.HasValue) query = query.Where(p => p.CriadoEm >= desde.Value);
        if (ate.HasValue)   query = query.Where(p => p.CriadoEm <= ate.Value);
        // E4 L2 (#3076): dia da entrega = AgendadoParaEm; sem ele (pedido para agora), CriadoEm.
        if (entregaDesde.HasValue) query = query.Where(p => (p.AgendadoParaEm ?? p.CriadoEm) >= entregaDesde.Value);
        if (entregaAte.HasValue)   query = query.Where(p => (p.AgendadoParaEm ?? p.CriadoEm) <= entregaAte.Value);

        if (string.IsNullOrWhiteSpace(search)) return query;
        var termo = search.Trim();
        var padrao = PadraoILike.Contendo(BuscaTextual.Normalizar(termo));
        var padraoLiteral = PadraoILike.Contendo(termo);
        var digitos = BuscaTextual.DigitosTelefone(termo);
        var buscaId = Guid.TryParse(termo, out var id);
        var buscaNumero = int.TryParse(termo.TrimStart('#'), out var numero);
        return query.Where(p =>
            (buscaId && p.Id == id) || (buscaNumero && p.NumeroDoDia == numero) ||
            (p.ClienteNome != null && EF.Functions.ILike(EasyStockDbContext.TraduzirTexto(p.ClienteNome.ToLower(), BuscaTextual.Acentos, BuscaTextual.SemAcentos), padrao, PadraoILike.Escape)) ||
            (p.ClienteApt  != null && EF.Functions.ILike(p.ClienteApt,  padraoLiteral, PadraoILike.Escape)) ||
            (p.ClienteTelefone != null && EF.Functions.ILike(p.ClienteTelefone, padraoLiteral, PadraoILike.Escape)) ||
            (digitos.Length >= 3 && p.ClienteTelefone != null && EasyStockDbContext.TraduzirTexto(p.ClienteTelefone, BuscaTextual.MascaraTelefone, "").Contains(digitos)) ||
            (p.Observacoes != null && EF.Functions.ILike(EasyStockDbContext.TraduzirTexto(p.Observacoes.ToLower(), BuscaTextual.Acentos, BuscaTextual.SemAcentos), padrao, PadraoILike.Escape)));
    }
}
