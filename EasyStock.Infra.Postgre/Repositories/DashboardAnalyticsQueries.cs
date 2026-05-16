using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Defaults;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasyStock.Infra.Postgre.Repositories;

/// <summary>Consultas de analytics para dashboard e movimentações.</summary>
internal sealed class DashboardAnalyticsQueries(EasyStockDbContext dbContext, IDistributedCache? cache = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private async Task<T?> GetCachedAsync<T>(string key)
    {
        if (cache is null) return default;
        var raw = await cache.GetStringAsync(key);
        return string.IsNullOrEmpty(raw) ? default : JsonSerializer.Deserialize<T>(raw, JsonOptions);
    }

    private async Task SetCachedAsync<T>(string key, T value, TimeSpan ttl)
    {
        if (cache is null) return;
        var serialized = JsonSerializer.Serialize(value, JsonOptions);
        await cache.SetStringAsync(key, serialized, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = ttl
        });
    }

    private static readonly TimeSpan DashboardTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MovimentacaoTtl = TimeSpan.FromMinutes(5);

    public async Task<DashboardResumo> GetDashboardResumoAsync(Guid empresaId, int periodoDias = 30, Guid? lojaId = null)
    {
        var cacheKey = $"analytics:dashboard:{empresaId}:{periodoDias}:{lojaId}";
        var cached = await GetCachedAsync<DashboardResumo>(cacheKey);
        if (cached is not null) return cached;

        var ate = DateTime.UtcNow;
        var de = ate.AddDays(-periodoDias);

        // Estoque
        var estoqueQuery = dbContext.ItensEstoque
            .AsNoTracking()
            .Where(i => i.EmpresaId == empresaId && i.Status != StatusItemEstoque.Vencido);
        if (lojaId.HasValue)
            estoqueQuery = estoqueQuery.Where(i => i.LojaId == lojaId.Value);

        var estoqueData = await estoqueQuery.Select(i => new
        {
            Quantidade = (int)i.QuantidadeAtual,
            ValorCusto = (decimal)i.CustoUnitario * (int)i.QuantidadeAtual,
            ValorVenda = ((decimal?)i.PrecoVendaSugerido ?? (decimal)i.CustoUnitario * OperacionalDefaults.FallbackMargemPrecoSugerido) * (int)i.QuantidadeAtual,
            EstaAbaixoMinimo = (int)i.QuantidadeAtual < i.QuantidadeMinima
        }).ToListAsync();

        var totalSkus = await estoqueQuery.Select(i => i.ProdutoId).Distinct().CountAsync();
        var totalQtd = estoqueData.Sum(e => e.Quantidade);
        var valorCusto = estoqueData.Sum(e => e.ValorCusto);
        var valorVenda = estoqueData.Sum(e => e.ValorVenda);
        var alertasBaixo = estoqueData.Count(e => e.EstaAbaixoMinimo);

        // Alertas de validade (30 dias)
        var cutoffValidade = DateTime.UtcNow.AddDays(30);
        var validadeQuery = dbContext.ItensEstoque.AsNoTracking()
            .Where(i => i.EmpresaId == empresaId && i.ValidadeEm != null && (DateTime?)i.ValidadeEm <= cutoffValidade);
        if (lojaId.HasValue)
            validadeQuery = validadeQuery.Where(i => i.LojaId == lojaId.Value);
        var alertasValidade = await validadeQuery.CountAsync();

        // Alertas de itens parados (30 dias sem movimento)
        var cutoffParado = DateTime.UtcNow.AddDays(-30);
        var paradosQuery = dbContext.ItensEstoque.AsNoTracking()
            .Where(i => i.EmpresaId == empresaId &&
                        (int)i.QuantidadeAtual > 0 &&
                        (i.UltimaMovimentacaoEm == null || i.UltimaMovimentacaoEm < cutoffParado));
        if (lojaId.HasValue)
            paradosQuery = paradosQuery.Where(i => i.LojaId == lojaId.Value);
        var alertasParados = await paradosQuery.CountAsync();

        // Receita do período — filtra Natureza=Venda para excluir Perda,
        // Prejuizo, Vencimento, Doacao e UsoInterno do cálculo de receita.
        var movQuery = dbContext.MovimentacoesEstoque
            .AsNoTracking()
            .Where(m => m.EmpresaId == empresaId &&
                m.Tipo == TipoMovimentacaoEstoque.Saida &&
                m.Natureza == NaturezaMovimentacaoEstoque.Venda &&
                m.DataMovimentacao >= de &&
                m.DataMovimentacao <= ate);
        if (lojaId.HasValue)
            movQuery = movQuery.Where(m => m.ItemEstoque != null && m.ItemEstoque.LojaId == lojaId.Value);

        var movData = await movQuery
            .Select(m => new { Quantidade = (int)m.Quantidade, ValorTotal = (decimal?)m.ValorTotal ?? 0m })
            .ToListAsync();

        var totalSaidasQtd = movData.Sum(m => m.Quantidade);
        var receitaEstimada = movData.Sum(m => m.ValorTotal);
        var dias = Math.Max(1, periodoDias);
        var mediaVendasDiaria = (decimal)totalSaidasQtd / dias;

        var result = new DashboardResumo(
            EmpresaId: empresaId,
            Periodo: periodoDias,
            TotalSkus: totalSkus,
            QuantidadeTotalEmEstoque: totalQtd,
            ValorTotalEstoque: Math.Round(valorVenda, 2),
            ValorCustoEstoque: Math.Round(valorCusto, 2),
            MediaVendasDiaria: Math.Round(mediaVendasDiaria, 2),
            // Arredondamento explicito AwayFromZero — default ToEven (banker's) seria
            // contraintuitivo num KPI de projeção de vendas para o usuário final.
            ProjecaoVendasPeriodo: Math.Round(mediaVendasDiaria * periodoDias, 0, MidpointRounding.AwayFromZero),
            ReceitaEstimadaPeriodo: Math.Round(receitaEstimada, 2),
            AlertasEstoqueBaixo: alertasBaixo,
            AlertasVencimento: alertasValidade,
            AlertasItensParados: alertasParados);

        await SetCachedAsync(cacheKey, result, DashboardTtl);
        return result;
    }

    public async Task<DashboardKpis> GetDashboardKpisAsync(Guid empresaId, DateTime de, DateTime ate, Guid? lojaId = null)
    {
        de = DateTime.SpecifyKind(de, DateTimeKind.Utc);
        ate = DateTime.SpecifyKind(ate, DateTimeKind.Utc);
        var periodoDias = Math.Max(1, (int)(ate - de).TotalDays);

        // Receita via Vendas (fonte canônica — fix BUG 1)
        var vendasQuery = dbContext.Vendas.AsNoTracking()
            .Where(v => v.EmpresaId == empresaId && v.DataVenda >= de && v.DataVenda <= ate);
        if (lojaId.HasValue)
            vendasQuery = vendasQuery.Where(v => v.LojaId == lojaId.Value);

        var vendasData = await vendasQuery.Select(v => new { Total = (decimal)v.ValorTotal }).ToListAsync();
        var receita = vendasData.Sum(v => v.Total);
        var totalVendas = vendasData.Count;
        var ticketMedio = totalVendas > 0 ? Math.Round(receita / totalVendas, 2) : 0m;

        // Custo via MovimentacoesEstoque (entradas no período para COGS aproximado)
        var custoQuery = dbContext.MovimentacoesEstoque.AsNoTracking()
            .Where(m => m.EmpresaId == empresaId &&
                m.Tipo == TipoMovimentacaoEstoque.Saida &&
                m.Natureza == NaturezaMovimentacaoEstoque.Venda &&
                m.DataMovimentacao >= de && m.DataMovimentacao <= ate);
        if (lojaId.HasValue)
            custoQuery = custoQuery.Where(m => m.ItemEstoque != null && m.ItemEstoque.LojaId == lojaId.Value);

        var custoVendas = await custoQuery
            .Where(m => m.ItemEstoque != null)
            .SumAsync(m => (decimal)m.ItemEstoque!.CustoUnitario * (int)m.Quantidade);

        // null = "margem nao calculavel" (sem receita OU sem custo informado nas movimentacoes).
        // Antes: custoVendas == 0 produzia margem 100% confiante mas falsa.
        decimal? margemBruta = (receita > 0 && custoVendas > 0)
            ? Math.Round((receita - custoVendas) / receita * 100m, 1)
            : null;

        // Pedidos
        var pedidosQuery = dbContext.Pedidos.AsNoTracking()
            .Where(p => p.EmpresaId == empresaId && p.CriadoEm >= de && p.CriadoEm <= ate);
        if (lojaId.HasValue)
            pedidosQuery = pedidosQuery.Where(p => p.LojaId == lojaId.Value);

        var pedidosData = await pedidosQuery.Select(p => new { p.Status }).ToListAsync();
        var pedidosTotal = pedidosData.Count;
        var pedidosEntregues = pedidosData.Count(p => p.Status == "entregue");
        var pedidosPendentes = pedidosData.Count(p => p.Status != "entregue" && p.Status != "cancelado");

        // Estoque (snapshot atual, não histórico). Filtra QuantidadeAtual > 0
        // pra alinhar com a base usada pelo GetEstoqueStatusDistribuicaoAsync
        // (donut "Situação do estoque" + insight "X% do estoque está crítico").
        // Antes esta query incluía lotes vazios (qty == 0) e podia dar 100%
        // crítico enquanto o donut mostrava 88% — duas verdades diferentes.
        var estoqueQuery = dbContext.ItensEstoque.AsNoTracking()
            .Where(i => i.EmpresaId == empresaId
                && i.Status != StatusItemEstoque.Vencido
                && i.Status != StatusItemEstoque.Descartado
                && (int)i.QuantidadeAtual > 0);
        if (lojaId.HasValue)
            estoqueQuery = estoqueQuery.Where(i => i.LojaId == lojaId.Value);

        var estoqueAgg = await estoqueQuery.GroupBy(_ => 1).Select(g => new
        {
            Total = g.Sum(i => (int)i.QuantidadeAtual),
            Custo = g.Sum(i => (decimal)i.CustoUnitario * (int)i.QuantidadeAtual),
            Critico = g.Count(i => i.Status == StatusItemEstoque.Critical
                                   || i.Status == StatusItemEstoque.Esgotado
                                   || (int)i.QuantidadeAtual < i.QuantidadeMinima),
            Count = g.Count()
        }).FirstOrDefaultAsync();

        var itensEmEstoque = estoqueAgg?.Total ?? 0;
        var custoEstoque = Math.Round(estoqueAgg?.Custo ?? 0m, 2);
        var pctCritico = estoqueAgg?.Count > 0
            ? Math.Round((decimal)(estoqueAgg.Critico) / estoqueAgg.Count * 100m, 1) : 0m;
        var lotesAtivos = estoqueAgg?.Count ?? 0;

        // Lotes finalizados
        var lotesQuery = dbContext.Lotes.AsNoTracking()
            .Where(l => l.EmpresaId == empresaId && l.Status == "finalizado" && l.DataProducao >= de && l.DataProducao <= ate);
        if (lojaId.HasValue)
            lotesQuery = lotesQuery.Where(l => l.LojaId == lojaId.Value);
        var lotesProduzidos = await lotesQuery.CountAsync();

        // Clientes ativos
        var clientesAtivos = await dbContext.Pedidos.AsNoTracking()
            .Where(p => p.EmpresaId == empresaId && p.CriadoEm >= de && p.CriadoEm <= ate && p.Status != "cancelado" && p.ClienteId != null)
            .Select(p => p.ClienteId)
            .Distinct()
            .CountAsync();

        return new DashboardKpis(
            Receita: Math.Round(receita, 2),
            TicketMedio: ticketMedio,
            Pedidos: pedidosTotal,
            PedidosEntregues: pedidosEntregues,
            PedidosPendentes: pedidosPendentes,
            ItensEmEstoque: itensEmEstoque,
            CustoEstoque: custoEstoque,
            MargemBruta: margemBruta,
            LotesProduzidos: lotesProduzidos,
            ClientesAtivos: clientesAtivos,
            PercentualCritico: pctCritico);
    }

    public async Task<EstoqueStatusDistribuicao> GetEstoqueStatusDistribuicaoAsync(Guid empresaId, Guid? lojaId = null)
    {
        var query = dbContext.ItensEstoque.AsNoTracking()
            .Where(i => i.EmpresaId == empresaId && (int)i.QuantidadeAtual > 0);
        if (lojaId.HasValue)
            query = query.Where(i => i.LojaId == lojaId.Value);

        var data = await query.GroupBy(i => i.Status).Select(g => new
        {
            Status = g.Key,
            Count = g.Count()
        }).ToListAsync();

        var ok = data.Where(d => d.Status == StatusItemEstoque.Ok).Sum(d => d.Count);
        var atencao = data.Where(d => d.Status == StatusItemEstoque.Warn).Sum(d => d.Count);
        var critico = data.Where(d => d.Status == StatusItemEstoque.Critical || d.Status == StatusItemEstoque.Esgotado).Sum(d => d.Count);
        var parado = data.Where(d => d.Status == StatusItemEstoque.Slow).Sum(d => d.Count);
        var total = data.Sum(d => d.Count);

        return new EstoqueStatusDistribuicao(ok, atencao, critico, parado, total);
    }

    public async Task<IReadOnlyList<PedidoPendenteResumo>> GetPedidosPendentesAsync(
        Guid empresaId, int periodoDias = 30, Guid? lojaId = null, int pageSize = 50)
    {
        var de = DateTime.UtcNow.AddDays(-periodoDias);
        var query = dbContext.Pedidos.AsNoTracking()
            .Include(p => p.Itens)
            .Include(p => p.Pagamentos)
            .Where(p => p.EmpresaId == empresaId && p.CriadoEm >= de && p.Status != "cancelado");
        if (lojaId.HasValue)
            query = query.Where(p => p.LojaId == lojaId.Value);

        var pedidos = await query.OrderByDescending(p => p.CriadoEm).Take(200).ToListAsync();

        return pedidos
            .Where(p => p.TotalPago < (decimal)p.Total)
            .Take(pageSize)
            .Select(p => new PedidoPendenteResumo(
                Id: p.Id,
                ClienteNome: p.ClienteNome,
                ItensResumo: string.Join(", ", p.Itens.Take(3).Select(i => i.Nome)) + (p.Itens.Count > 3 ? $" +{p.Itens.Count - 3}" : ""),
                Total: (decimal)p.Total,
                TotalPago: p.TotalPago,
                EmAberto: (decimal)p.Total - p.TotalPago,
                CriadoEm: p.CriadoEm,
                Status: p.Status))
            .ToList();
    }

    public async Task<int> GetEntreguesSemVendaCountAsync(Guid empresaId, int periodoDias = 30, Guid? lojaId = null)
    {
        var de = DateTime.UtcNow.AddDays(-periodoDias);
        var query = dbContext.Pedidos.AsNoTracking()
            .Where(p => p.EmpresaId == empresaId && p.Status == "entregue" && p.VendaId == null && p.CriadoEm >= de);
        if (lojaId.HasValue)
            query = query.Where(p => p.LojaId == lojaId.Value);
        return await query.CountAsync();
    }

    public async Task<int> GetLotesFinalizadosCountAsync(Guid empresaId, int periodoDias = 30, Guid? lojaId = null)
    {
        var de = DateTime.UtcNow.AddDays(-periodoDias);
        var query = dbContext.Lotes.AsNoTracking()
            .Where(l => l.EmpresaId == empresaId && l.Status == "finalizado" && l.DataProducao >= de);
        if (lojaId.HasValue)
            query = query.Where(l => l.LojaId == lojaId.Value);
        return await query.CountAsync();
    }

    public async Task<int> GetClientesAtivosCountAsync(Guid empresaId, int periodoDias = 30, Guid? lojaId = null)
    {
        var de = DateTime.UtcNow.AddDays(-periodoDias);
        var query = dbContext.Pedidos.AsNoTracking()
            .Where(p => p.EmpresaId == empresaId && p.CriadoEm >= de && p.Status != "cancelado" && p.ClienteId != null);
        if (lojaId.HasValue)
            query = query.Where(p => p.LojaId == lojaId.Value);
        return await query.Select(p => p.ClienteId).Distinct().CountAsync();
    }

    public async Task<IReadOnlyList<AlertaEstoqueResumo>> GetItensCriticosResumoAsync(
        Guid empresaId, int top = 20, Guid? lojaId = null)
    {
        var query = dbContext.ItensEstoque.AsNoTracking()
            .Include(i => i.Produto)
            .Where(i => i.EmpresaId == empresaId &&
                (i.Status == StatusItemEstoque.Critical ||
                 i.Status == StatusItemEstoque.Esgotado ||
                 (int)i.QuantidadeAtual <= i.QuantidadeMinima));
        if (lojaId.HasValue)
            query = query.Where(i => i.LojaId == lojaId.Value);

        var itens = await query
            .OrderBy(i => (int)i.QuantidadeAtual)
            .Take(top)
            .Select(i => new
            {
                i.Id,
                ProdutoId = i.ProdutoId,
                NomeProduto = i.Produto != null ? i.Produto.Nome : null,
                Quantidade = (int)i.QuantidadeAtual,
            })
            .ToListAsync();

        return itens.Select(i => new AlertaEstoqueResumo(
            ItemEstoqueId: i.Id,
            ProdutoId: i.ProdutoId,
            NomeProduto: i.NomeProduto,
            Tipo: "critico",
            Quantidade: i.Quantidade,
            Dias: 0))
            .ToList();
    }

    public async Task<IReadOnlyList<ReceitaCustoDia>> GetReceitaCustoSerieAsync(
        Guid empresaId, DateTime de, DateTime ate, Guid? lojaId = null)
    {
        de = DateTime.SpecifyKind(de, DateTimeKind.Utc);
        ate = DateTime.SpecifyKind(ate, DateTimeKind.Utc);
        var periodoDias = Math.Max(1, (int)(ate - de).TotalDays);
        var porDia = periodoDias <= 30;

        var cacheKey = $"analytics:receita-custo:{empresaId}:{de:yyyyMMdd}:{ate:yyyyMMdd}:{lojaId}";
        var cached = await GetCachedAsync<List<ReceitaCustoDia>>(cacheKey);
        if (cached is not null) return cached;

        // Receita por dia ou mês
        var vendasQuery = dbContext.Vendas.AsNoTracking()
            .Where(v => v.EmpresaId == empresaId && v.DataVenda >= de && v.DataVenda <= ate);
        if (lojaId.HasValue)
            vendasQuery = vendasQuery.Where(v => v.LojaId == lojaId.Value);

        var vendasRaw = await vendasQuery
            .Select(v => new { v.DataVenda, Valor = (decimal)v.ValorTotal })
            .ToListAsync();

        // Custo (entradas de estoque no período)
        var custoQuery = dbContext.MovimentacoesEstoque.AsNoTracking()
            .Where(m => m.EmpresaId == empresaId &&
                m.Tipo == TipoMovimentacaoEstoque.Entrada &&
                m.DataMovimentacao >= de && m.DataMovimentacao <= ate);
        if (lojaId.HasValue)
            custoQuery = custoQuery.Where(m => m.ItemEstoque != null && m.ItemEstoque.LojaId == lojaId.Value);

        var custoRaw = await custoQuery
            .Select(m => new { m.DataMovimentacao, Valor = (decimal?)m.ValorTotal ?? 0m })
            .ToListAsync();

        // Buckets indexados por DateTime para ordenacao cronologica correta.
        // Antes usava SortedDictionary<string,...> com chave "dd/MM" — ordenacao
        // alfabetica colocava 01/05 antes de 15/04 e o grafico mostrava abril
        // depois de maio no eixo X.
        var buckets = new SortedDictionary<DateTime, (decimal Receita, decimal Custo)>();
        var fmtLabel = porDia ? "dd/MM" : "MM/yyyy";
        DateTime BucketKey(DateTime d) => porDia
            ? d.Date
            : new DateTime(d.Year, d.Month, 1);

        if (porDia)
        {
            for (var d = de.Date; d <= ate.Date; d = d.AddDays(1))
                buckets[d] = (0m, 0m);
        }
        else
        {
            for (var d = new DateTime(de.Year, de.Month, 1); d <= ate; d = d.AddMonths(1))
                buckets[d] = (0m, 0m);
        }

        foreach (var v in vendasRaw)
        {
            var k = BucketKey(porDia ? v.DataVenda.ToLocalTime() : v.DataVenda);
            if (buckets.TryGetValue(k, out var b)) buckets[k] = (b.Receita + v.Valor, b.Custo);
        }
        foreach (var c in custoRaw)
        {
            var k = BucketKey(porDia ? c.DataMovimentacao.ToLocalTime() : c.DataMovimentacao);
            if (buckets.TryGetValue(k, out var b)) buckets[k] = (b.Receita, b.Custo + c.Valor);
        }

        var result = buckets.Select(kvp => new ReceitaCustoDia(
            Label: kvp.Key.ToString(fmtLabel),
            Receita: Math.Round(kvp.Value.Receita, 2),
            Custo: Math.Round(kvp.Value.Custo, 2),
            Lucro: Math.Round(kvp.Value.Receita - kvp.Value.Custo, 2)
        )).ToList();

        await SetCachedAsync(cacheKey, result, DashboardTtl);
        return result;
    }

    public async Task<IReadOnlyList<FluxoCaixaDia>> GetFluxoCaixaAsync(
        Guid empresaId, DateTime de, DateTime ate, Guid? lojaId = null)
    {
        de = DateTime.SpecifyKind(de, DateTimeKind.Utc);
        ate = DateTime.SpecifyKind(ate, DateTimeKind.Utc);

        var cacheKey = $"analytics:fluxo-caixa:{empresaId}:{de:yyyyMMdd}:{ate:yyyyMMdd}:{lojaId}";
        var cached = await GetCachedAsync<List<FluxoCaixaDia>>(cacheKey);
        if (cached is not null) return cached;

        var deDate = DateOnly.FromDateTime(de);
        var ateDate = DateOnly.FromDateTime(ate);

        var query = dbContext.FechamentosCaixa.AsNoTracking()
            .Where(f => f.EmpresaId == empresaId && f.Data >= deDate && f.Data <= ateDate);
        if (lojaId.HasValue)
            query = query.Where(f => f.LojaId == lojaId.Value);

        var fechamentos = await query.OrderBy(f => f.Data).ToListAsync();

        decimal saldoAcum = 0m;
        var result = fechamentos.Select(f =>
        {
            var entradas = f.TotalVendas + f.TotalPagamentosPedidos + f.TotalEntradasExtras;
            var saidas = f.TotalSaidasExtras;
            saldoAcum += entradas - saidas;
            return new FluxoCaixaDia(
                Label: f.Data.ToString("dd/MM"),
                Entradas: Math.Round(entradas, 2),
                Saidas: Math.Round(saidas, 2),
                SaldoAcumulado: Math.Round(saldoAcum, 2));
        }).ToList();

        await SetCachedAsync(cacheKey, result, DashboardTtl);
        return result;
    }

    public async Task<IReadOnlyList<ValidadeSemanaItem>> GetValidadeTimelineAsync(
        Guid empresaId, Guid? lojaId = null)
    {
        var cacheKey = $"analytics:validade-timeline:{empresaId}:{lojaId}";
        var cached = await GetCachedAsync<List<ValidadeSemanaItem>>(cacheKey);
        if (cached is not null) return cached;

        var now = DateTime.UtcNow;
        var limite = now.AddDays(28);

        var query = dbContext.ItensEstoque.AsNoTracking()
            .Where(i => i.EmpresaId == empresaId &&
                i.ValidadeEm != null &&
                (DateTime?)i.ValidadeEm > now &&
                (DateTime?)i.ValidadeEm <= limite &&
                (int)i.QuantidadeAtual > 0);
        if (lojaId.HasValue)
            query = query.Where(i => i.LojaId == lojaId.Value);

        var itens = await query
            .Select(i => new
            {
                ProdutoId = i.ProdutoId,
                ValidadeEm = (DateTime)i.ValidadeEm!,
                Quantidade = (int)i.QuantidadeAtual
            })
            .ToListAsync();

        // Busca nomes separado para evitar JOIN complexo
        var produtoIds = itens.Select(i => i.ProdutoId).Distinct().ToList();
        var nomes = await dbContext.Produtos.AsNoTracking()
            .Where(p => produtoIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Nome);

        var buckets = new Dictionary<int, (List<string> Nomes, int Qtd)>
        {
            [1] = (new List<string>(), 0),
            [2] = (new List<string>(), 0),
            [3] = (new List<string>(), 0),
            [4] = (new List<string>(), 0),
        };

        foreach (var item in itens)
        {
            var dias = (item.ValidadeEm - now.Date).TotalDays;
            var semana = Math.Min(4, Math.Max(1, (int)Math.Ceiling(dias / 7.0)));
            var b = buckets[semana];
            var nome = nomes.TryGetValue(item.ProdutoId, out var n) ? n : "Produto";
            b.Nomes.Add(nome);
            buckets[semana] = (b.Nomes, b.Qtd + item.Quantidade);
        }

        var result = buckets.Select(kvp => new ValidadeSemanaItem(
            Semana: $"Semana {kvp.Key}",
            Quantidade: kvp.Value.Qtd,
            NomesProdutos: kvp.Value.Nomes.Distinct().Take(5).ToArray(),
            DiasMedia: kvp.Key * 7 - 3))
            .ToList();

        await SetCachedAsync(cacheKey, result, DashboardTtl);
        return result;
    }

    public async Task<IReadOnlyList<TopProdutoDashboard>> GetTopProdutosAsync(
        Guid empresaId, DateTime de, DateTime ate, int top = 5, Guid? lojaId = null)
    {
        de = DateTime.SpecifyKind(de, DateTimeKind.Utc);
        ate = DateTime.SpecifyKind(ate, DateTimeKind.Utc);

        var cacheKey = $"analytics:top-produtos:{empresaId}:{de:yyyyMMdd}:{ate:yyyyMMdd}:{top}:{lojaId}";
        var cached = await GetCachedAsync<List<TopProdutoDashboard>>(cacheKey);
        if (cached is not null) return cached;

        var query = dbContext.MovimentacoesEstoque.AsNoTracking()
            .Where(m => m.EmpresaId == empresaId &&
                m.Tipo == TipoMovimentacaoEstoque.Saida &&
                m.Natureza == NaturezaMovimentacaoEstoque.Venda &&
                m.DataMovimentacao >= de && m.DataMovimentacao <= ate &&
                m.ItemEstoque != null);
        if (lojaId.HasValue)
            query = query.Where(m => m.ItemEstoque != null && m.ItemEstoque.LojaId == lojaId.Value);

        var raw = await query
            .Select(m => new
            {
                ProdutoId = m.ItemEstoque!.ProdutoId,
                Quantidade = (int)m.Quantidade,
                Valor = (decimal?)m.ValorTotal ?? 0m
            })
            .ToListAsync();

        var produtoIds = raw.Select(m => m.ProdutoId).Distinct().ToList();
        var nomes = await dbContext.Produtos.AsNoTracking()
            .Where(p => produtoIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Nome);

        var result = raw
            .GroupBy(m => m.ProdutoId)
            .Select(g => new TopProdutoDashboard(
                ProdutoId: g.Key,
                Nome: nomes.TryGetValue(g.Key, out var n) ? n : "Produto",
                Quantidade: g.Sum(m => m.Quantidade),
                Receita: Math.Round(g.Sum(m => m.Valor), 2)))
            .OrderByDescending(x => x.Quantidade)
            .Take(top)
            .ToList();

        await SetCachedAsync(cacheKey, result, DashboardTtl);
        return result;
    }

    public async Task<IReadOnlyList<TopClienteDashboard>> GetTopClientesAsync(
        Guid empresaId, DateTime de, DateTime ate, int top = 5, Guid? lojaId = null)
    {
        de = DateTime.SpecifyKind(de, DateTimeKind.Utc);
        ate = DateTime.SpecifyKind(ate, DateTimeKind.Utc);

        var cacheKey = $"analytics:top-clientes:{empresaId}:{de:yyyyMMdd}:{ate:yyyyMMdd}:{top}:{lojaId}";
        var cached = await GetCachedAsync<List<TopClienteDashboard>>(cacheKey);
        if (cached is not null) return cached;

        var query = dbContext.Pedidos.AsNoTracking()
            .Where(p => p.EmpresaId == empresaId &&
                p.CriadoEm >= de && p.CriadoEm <= ate &&
                p.Status != "cancelado" &&
                p.ClienteId != null);
        if (lojaId.HasValue)
            query = query.Where(p => p.LojaId == lojaId.Value);

        var pedidos = await query
            .Select(p => new
            {
                ClienteId = p.ClienteId!.Value,
                ClienteNome = p.ClienteNome,
                TotalPago = p.Pagamentos.Sum(pg => pg.Valor)
            })
            .ToListAsync();

        var result = pedidos
            .GroupBy(p => new { p.ClienteId, p.ClienteNome })
            .Select(g => new TopClienteDashboard(
                ClienteId: g.Key.ClienteId,
                Nome: g.Key.ClienteNome ?? "Cliente",
                TotalPago: Math.Round(g.Sum(p => p.TotalPago), 2),
                Pedidos: g.Count(),
                TicketMedio: g.Count() > 0 ? Math.Round(g.Sum(p => p.TotalPago) / g.Count(), 2) : 0m))
            .OrderByDescending(x => x.TotalPago)
            .Take(top)
            .ToList();

        await SetCachedAsync(cacheKey, result, DashboardTtl);
        return result;
    }

    public async Task<IReadOnlyList<ProducaoPorOperador>> GetProducaoPorOperadorAsync(
        Guid empresaId, DateTime de, DateTime ate, Guid? lojaId = null)
    {
        de = DateTime.SpecifyKind(de, DateTimeKind.Utc);
        ate = DateTime.SpecifyKind(ate, DateTimeKind.Utc);

        var cacheKey = $"analytics:producao-operador:{empresaId}:{de:yyyyMMdd}:{ate:yyyyMMdd}:{lojaId}";
        var cached = await GetCachedAsync<List<ProducaoPorOperador>>(cacheKey);
        if (cached is not null) return cached;

        var query = dbContext.Lotes.AsNoTracking()
            .Where(l => l.EmpresaId == empresaId &&
                l.Status == "finalizado" &&
                l.DataProducao >= de && l.DataProducao <= ate);
        if (lojaId.HasValue)
            query = query.Where(l => l.LojaId == lojaId.Value);

        // Projeta Etiquetas.Count via subquery (1 SQL agregado) ao invés de Include massivo.
        var raw = await query
            .Select(l => new { l.OperadorNome, EtiquetasCount = l.Etiquetas.Count })
            .ToListAsync();

        var result = raw
            .GroupBy(l => l.OperadorNome ?? "Desconhecido")
            .Select(g => new ProducaoPorOperador(
                Operador: g.Key,
                Lotes: g.Count(),
                Unidades: g.Sum(l => l.EtiquetasCount)))
            .OrderByDescending(x => x.Lotes)
            .ToList();

        await SetCachedAsync(cacheKey, result, DashboardTtl);
        return result;
    }

    public async Task<IReadOnlyList<EntradasSaidasSemana>> GetEntradasSaidasSemanalAsync(
        Guid empresaId, DateTime de, DateTime ate, Guid? lojaId = null)
    {
        de = DateTime.SpecifyKind(de, DateTimeKind.Utc);
        ate = DateTime.SpecifyKind(ate, DateTimeKind.Utc);

        var cacheKey = $"analytics:entradas-saidas-semanal:{empresaId}:{de:yyyyMMdd}:{ate:yyyyMMdd}:{lojaId}";
        var cached = await GetCachedAsync<List<EntradasSaidasSemana>>(cacheKey);
        if (cached is not null) return cached;

        var query = dbContext.MovimentacoesEstoque.AsNoTracking()
            .Where(m => m.EmpresaId == empresaId &&
                m.DataMovimentacao >= de && m.DataMovimentacao <= ate);
        if (lojaId.HasValue)
            query = query.Where(m => m.ItemEstoque != null && m.ItemEstoque.LojaId == lojaId.Value);

        var raw = await query
            .Select(m => new { m.Tipo, m.DataMovimentacao, Valor = (decimal?)m.ValorTotal ?? 0m })
            .ToListAsync();

        var totalDias = Math.Max(1, (int)(ate - de).TotalDays);
        var nSemanas = Math.Min(12, Math.Max(1, (int)Math.Ceiling(totalDias / 7.0)));

        var buckets = new SortedDictionary<int, (decimal Entradas, decimal Saidas)>();
        for (var w = 1; w <= nSemanas; w++) buckets[w] = (0m, 0m);

        foreach (var m in raw)
        {
            var diasFromStart = Math.Max(0, (m.DataMovimentacao - de).TotalDays);
            var semana = Math.Min(nSemanas, Math.Max(1, (int)Math.Ceiling((diasFromStart + 1) / 7.0)));
            var b = buckets[semana];
            if (m.Tipo == TipoMovimentacaoEstoque.Entrada)
                buckets[semana] = (b.Entradas + m.Valor, b.Saidas);
            else
                buckets[semana] = (b.Entradas, b.Saidas + m.Valor);
        }

        var result = buckets
            .Select(kvp => new EntradasSaidasSemana(
                Label: $"Sem {kvp.Key}",
                Entradas: Math.Round(kvp.Value.Entradas, 2),
                Saidas: Math.Round(kvp.Value.Saidas, 2)))
            .ToList();

        await SetCachedAsync(cacheKey, result, DashboardTtl);
        return result;
    }

    public async Task<FornecedoresResumo> GetFornecedoresResumoAsync(
        Guid empresaId, Guid? lojaId = null)
    {
        var cacheKey = $"analytics:fornecedores:{empresaId}:{lojaId}";
        var cached = await GetCachedAsync<FornecedoresResumo>(cacheKey);
        if (cached is not null) return cached;

        var lista = await dbContext.Fornecedores.AsNoTracking()
            .Where(f => f.EmpresaId == empresaId)
            .OrderBy(f => f.Nome)
            .Select(f => new { f.Id, f.Nome, f.Ativo })
            .ToListAsync();

        var ativos = lista.Count(f => f.Ativo);
        var inativos = lista.Count(f => !f.Ativo);
        var items = lista
            .Select(f => new FornecedorResumoItem(f.Id, f.Nome, f.Ativo))
            .ToList();

        var result = new FornecedoresResumo(ativos, inativos, items);
        await SetCachedAsync(cacheKey, result, DashboardTtl);
        return result;
    }

    public async Task<IReadOnlyList<NovosClientesMes>> GetNovosClientesPorMesAsync(
        Guid empresaId, int meses = 6, Guid? lojaId = null)
    {
        var cacheKey = $"analytics:novos-clientes:{empresaId}:{meses}:{lojaId}";
        var cached = await GetCachedAsync<List<NovosClientesMes>>(cacheKey);
        if (cached is not null) return cached;

        var ate = DateTime.UtcNow;
        // Início do primeiro bucket: meses-1 meses atrás, dia 1, para retornar exatamente `meses` buckets.
        var primeiroMes = new DateTime(ate.Year, ate.Month, 1).AddMonths(-(meses - 1));
        var de = DateTime.SpecifyKind(primeiroMes, DateTimeKind.Utc);

        var clientes = await dbContext.Clientes.AsNoTracking()
            .Where(c => c.EmpresaId == empresaId && c.CriadoEm >= de)
            .Select(c => new { c.CriadoEm })
            .ToListAsync();

        var buckets = new SortedDictionary<string, int>();
        for (var d = primeiroMes; d <= ate; d = d.AddMonths(1))
            buckets[d.ToString("MM/yyyy")] = 0;

        foreach (var c in clientes)
        {
            var k = c.CriadoEm.ToString("MM/yyyy");
            if (buckets.ContainsKey(k)) buckets[k]++;
        }

        var result = buckets
            .Select(kvp => new NovosClientesMes(kvp.Key, kvp.Value))
            .ToList();

        await SetCachedAsync(cacheKey, result, DashboardTtl);
        return result;
    }

    public async Task<IReadOnlyList<MovimentacaoResumo>> GetMovimentacoesResumoAsync(
        Guid empresaId,
        DateTime de,
        DateTime ate,
        TipoMovimentacaoEstoque? tipo = null,
        Guid? lojaId = null)
    {
        var cacheKey = $"analytics:movimentacoes:{empresaId}:{de:yyyyMMdd}:{ate:yyyyMMdd}:{tipo}:{lojaId}";
        var cached = await GetCachedAsync<List<MovimentacaoResumo>>(cacheKey);
        if (cached is not null) return cached;

        var query = dbContext.MovimentacoesEstoque
            .AsNoTracking()
            .Where(m => m.EmpresaId == empresaId &&
                m.DataMovimentacao >= DateTime.SpecifyKind(de, DateTimeKind.Utc) &&
                m.DataMovimentacao <= DateTime.SpecifyKind(ate, DateTimeKind.Utc));

        if (tipo.HasValue)
            query = query.Where(m => m.Tipo == tipo.Value);
        if (lojaId.HasValue)
            query = query.Where(m => m.ItemEstoque != null && m.ItemEstoque.LojaId == lojaId.Value);

        var raw = await query
            .Select(m => new
            {
                m.DataMovimentacao.Year,
                m.DataMovimentacao.Month,
                m.DataMovimentacao.Day,
                m.Tipo,
                Quantidade = (int)m.Quantidade,
                Valor = (decimal?)m.ValorTotal ?? 0m
            })
            .ToListAsync();

        var result = raw
            .GroupBy(m => new { m.Year, m.Month, m.Day, m.Tipo })
            .Select(g => new MovimentacaoResumo(
                Ano: g.Key.Year,
                Mes: g.Key.Month,
                Dia: g.Key.Day,
                Tipo: g.Key.Tipo,
                TotalMovimentacoes: g.Count(),
                QuantidadeTotal: g.Sum(m => m.Quantidade),
                ValorTotal: Math.Round(g.Sum(m => m.Valor), 2)))
            .OrderBy(x => x.Ano).ThenBy(x => x.Mes).ThenBy(x => x.Dia).ThenBy(x => x.Tipo)
            .ToList();

        await SetCachedAsync(cacheKey, result, MovimentacaoTtl);
        return result;
    }
}
