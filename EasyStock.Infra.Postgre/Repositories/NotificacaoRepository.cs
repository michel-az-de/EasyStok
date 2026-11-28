using System.Text.Json;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Services.Notifications;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories
{
    public sealed class NotificacaoRepository(EasyStockDbContext dbContext)
        : INotificacaoRepository
    {
        public Task<Notificacao?> GetByIdAsync(Guid id) =>
            dbContext.Notificacoes.FirstOrDefaultAsync(n => n.Id == id);

        public async Task<(IEnumerable<Notificacao> Items, int TotalCount)> GetByEmpresaAsync(
            Guid empresaId,
            bool? lida = null,
            TipoAlertaEstoque? tipo = null,
            SeveridadeNotificacao? severidade = null,
            int page = 1,
            int pageSize = 20,
            Guid? usuarioId = null)
        {
            var query = dbContext.Notificacoes
                .AsNoTracking()
                .Where(n => n.EmpresaId == empresaId && (!usuarioId.HasValue || n.UsuarioId == null || n.UsuarioId == usuarioId));

            if (lida.HasValue)
                query = query.Where(n => n.Lida == lida.Value);
            if (tipo.HasValue)
                query = FiltrarPorTipoExibido(query, tipo.Value);
            if (severidade.HasValue)
                query = query.Where(n => n.Severidade == severidade.Value);

            var totalCount = await query.CountAsync();
            var items = await query
                .OrderByDescending(n => n.CriadaEm)
                .ThenBy(n => n.Severidade)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            await ExibirLegadosAsync(empresaId, items);
            return (items, totalCount);
        }

        public async Task<IEnumerable<Notificacao>> GetRecentesNaoLidasAsync(Guid empresaId, int limit = 5, Guid? usuarioId = null)
        {
            var items = await dbContext.Notificacoes
                .AsNoTracking()
                .Where(n => n.EmpresaId == empresaId && !n.Lida && (!usuarioId.HasValue || n.UsuarioId == null || n.UsuarioId == usuarioId))
                .OrderByDescending(n => n.CriadaEm)
                .ThenBy(n => n.Severidade)
                .Take(limit)
                .ToListAsync();
            await ExibirLegadosAsync(empresaId, items);
            return items;
        }

        // Envios antigos usavam o título e o tipo do enum de estoque, mesmo para avisos de caixa.
        // Recupera o assunto do envio e o tipo operacional apenas nas cópias AsNoTracking, preservando o registro histórico.
        private async Task ExibirLegadosAsync(Guid empresaId, List<Notificacao> items)
        {
            var legadas = items.Where(n => n.OutboxMensagemId.HasValue
                && ((n.TipoAlerta == TipoAlertaEstoque.ValidadeProxima && n.Titulo == "Validade Próxima")
                    || (n.TipoAlerta == TipoAlertaEstoque.PedidoRecebido && n.Titulo == "Pedido Recebido"))).ToList();
            if (legadas.Count > 0)
            {
                var ids = legadas.Select(n => n.OutboxMensagemId!.Value).ToArray();
                var assuntos = await dbContext.NotifOutboxMensagens.AsNoTracking()
                    .Where(m => m.EmpresaId == empresaId && m.Canal == CanalNotificacao.InApp && ids.Contains(m.Id))
                    .Select(m => new { m.Id, m.AssuntoRenderizado })
                    .ToDictionaryAsync(m => m.Id, m => m.AssuntoRenderizado);
                foreach (var aviso in legadas)
                    if (assuntos.TryGetValue(aviso.OutboxMensagemId!.Value, out var assunto) && !string.IsNullOrWhiteSpace(assunto))
                        aviso.Titulo = assunto;
            }

            foreach (var aviso in items)
                aviso.TipoAlerta = TipoExibido(aviso.TipoAlerta, aviso.OutboxMensagemId.HasValue);
        }

        // #3402: só o InAppCanal grava OutboxMensagemId, e antes da #2304 o aviso operacional saía como ValidadeProxima.
        // O alerta real de validade não tem envio. Lista, filtro e resumo usam o mesmo tipo; sem isto, o sino do Console
        // mandaria o aviso de caixa ao Estoque do dia.
        private static TipoAlertaEstoque TipoExibido(TipoAlertaEstoque tipo, bool doEnvio) =>
            doEnvio && tipo == TipoAlertaEstoque.ValidadeProxima ? TipoAlertaEstoque.AvisoOperacional : tipo;

        private static IQueryable<Notificacao> FiltrarPorTipoExibido(IQueryable<Notificacao> query, TipoAlertaEstoque tipo) => tipo switch
        {
            TipoAlertaEstoque.ValidadeProxima => query.Where(n => n.TipoAlerta == tipo && n.OutboxMensagemId == null),
            TipoAlertaEstoque.AvisoOperacional => query.Where(n => n.TipoAlerta == tipo
                || (n.TipoAlerta == TipoAlertaEstoque.ValidadeProxima && n.OutboxMensagemId != null)),
            _ => query.Where(n => n.TipoAlerta == tipo),
        };

        public async Task<NotificacaoResumo> GetResumoAsync(Guid empresaId, Guid? usuarioId = null)
        {
            var gruposPorSeveridade = await dbContext.Notificacoes
                .Where(n => n.EmpresaId == empresaId && !n.Lida && (!usuarioId.HasValue || n.UsuarioId == null || n.UsuarioId == usuarioId))
                .GroupBy(n => n.Severidade)
                .Select(g => new { Severidade = g.Key, Count = g.Count() })
                .ToListAsync();

            var gruposPorTipo = await dbContext.Notificacoes
                .Where(n => n.EmpresaId == empresaId && !n.Lida && (!usuarioId.HasValue || n.UsuarioId == null || n.UsuarioId == usuarioId))
                .GroupBy(n => new { n.TipoAlerta, DoEnvio = n.OutboxMensagemId != null })
                .Select(g => new { Tipo = g.Key.TipoAlerta, g.Key.DoEnvio, Count = g.Count() })
                .ToListAsync();

            var porTipo = gruposPorTipo
                .GroupBy(g => TipoExibido(g.Tipo, g.DoEnvio).ToString())
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Count));

            return new NotificacaoResumo
            {
                TotalNaoLidas = gruposPorSeveridade.Sum(g => g.Count),
                Criticas = gruposPorSeveridade.FirstOrDefault(g => g.Severidade == SeveridadeNotificacao.Critica)?.Count ?? 0,
                Altas = gruposPorSeveridade.FirstOrDefault(g => g.Severidade == SeveridadeNotificacao.Alta)?.Count ?? 0,
                Medias = gruposPorSeveridade.FirstOrDefault(g => g.Severidade == SeveridadeNotificacao.Media)?.Count ?? 0,
                Informativas = gruposPorSeveridade.FirstOrDefault(g => g.Severidade == SeveridadeNotificacao.Informativa)?.Count ?? 0,
                PorTipo = porTipo
            };
        }

        public async Task<IReadOnlyList<AlertaAutomatico>> GetAlertasAutomaticosAsync(
            Guid empresaId, IReadOnlyCollection<TipoAlertaEstoque> tipos, DateTime desdeUtc, CancellationToken ct = default) =>
            await dbContext.Notificacoes
                .AsNoTracking()
                .Where(n => n.EmpresaId == empresaId && n.UsuarioId == null && n.OutboxMensagemId == null
                    && tipos.Contains(n.TipoAlerta) && (!n.Lida || n.CriadaEm >= desdeUtc))
                .Select(n => new AlertaAutomatico(n.Id, n.TipoAlerta, n.ReferenciaId, n.Lida, n.CriadaEm))
                .ToListAsync(ct);

        public Task<int> RemoverAlertasAsync(Guid empresaId, IReadOnlyCollection<Guid> ids, CancellationToken ct = default) =>
            ids.Count == 0
                ? Task.FromResult(0)
                : dbContext.Notificacoes
                    .Where(n => n.EmpresaId == empresaId && n.UsuarioId == null && n.OutboxMensagemId == null && ids.Contains(n.Id))
                    .ExecuteDeleteAsync(ct);

        public Task<int> ExpurgarAlertasAutomaticosAsync(
            Guid empresaId, IReadOnlyCollection<TipoAlertaEstoque> tipos, DateTime lidosAntesDeUtc, DateTime qualquerAntesDeUtc,
            CancellationToken ct = default) =>
            dbContext.Notificacoes
                .Where(n => n.EmpresaId == empresaId && n.UsuarioId == null && n.OutboxMensagemId == null
                    && tipos.Contains(n.TipoAlerta)
                    && ((n.Lida && n.CriadaEm < lidosAntesDeUtc) || n.CriadaEm < qualquerAntesDeUtc))
                .ExecuteDeleteAsync(ct);

        // #2258 (F1). Cross-tenant quando empresaId é nulo (job, sob o bypass de RLS): IgnoreQueryFilters desliga o filtro
        // do EF e o resto é escopo explícito. Parte dos avisos NÃO LIDOS (poucos) e só depois pergunta quais aberturas
        // fecharam, em vez de varrer todas as aberturas já avisadas.
        public async Task<int> ResolverAvisosDeCaixaFechadoAsync(Guid? empresaId = null, CancellationToken ct = default)
        {
            var prefixoPrazo = PrazoEstouradoEvento.PrefixoCorrelationId(TipoPrazo.CaixaEsquecido);

            var candidatos = await (
                from n in dbContext.Notificacoes.IgnoreQueryFilters().AsNoTracking()
                join o in dbContext.NotifOutboxMensagens.IgnoreQueryFilters() on n.OutboxMensagemId equals (Guid?)o.Id
                join e in dbContext.NotifEventos.IgnoreQueryFilters() on o.EventoId equals e.Id
                where !n.Lida && (empresaId == null || n.EmpresaId == empresaId)
                      && (e.Tipo == TipoEventoNotificacao.CaixaAbertoEsquecido
                          || (e.Tipo == TipoEventoNotificacao.PrazoEstourado && e.CorrelationId.StartsWith(prefixoPrazo)))
                select new { AvisoId = n.Id, n.EmpresaId, e.RefEntidadeId, e.PayloadJson })
                .ToListAsync(ct);

            // Aviso antigo do sino não guardava a abertura no evento: ela vem no payload.
            var avisos = candidatos
                .Select(c => (c.AvisoId, c.EmpresaId, AberturaId: c.RefEntidadeId ?? LerAberturaId(c.PayloadJson)))
                .Where(c => c.AberturaId is not null)
                .ToList();
            if (avisos.Count == 0) return 0;

            var aberturaIds = avisos.Select(a => a.AberturaId!.Value).Distinct().ToList();
            var fechadas = (await dbContext.MovimentosCaixa.IgnoreQueryFilters().AsNoTracking()
                    // #2258 (banca): a abertura tem de ser da mesma empresa do aviso, além do RLS da sessão.
                    .Where(a => a.Tipo == "abertura" && aberturaIds.Contains(a.Id)
                             && (empresaId == null || a.EmpresaId == empresaId)
                             && dbContext.MovimentosCaixa.IgnoreQueryFilters().Any(f =>
                                    f.Tipo == "fechamento" && f.EstornadoEm == null
                                    && f.EmpresaId == a.EmpresaId && f.LojaId == a.LojaId
                                    && f.DataMovimento > a.DataMovimento))
                    .Select(a => new { a.Id, a.EmpresaId })
                    .ToListAsync(ct))
                .Select(a => (a.Id, a.EmpresaId))
                .ToHashSet();

            var resolver = avisos.Where(a => fechadas.Contains((a.AberturaId!.Value, a.EmpresaId))).Select(a => a.AvisoId).ToList();
            if (resolver.Count == 0) return 0;

            var agora = DateTime.UtcNow;
            return await dbContext.Notificacoes.IgnoreQueryFilters()
                .Where(n => resolver.Contains(n.Id) && !n.Lida)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(n => n.Lida, true)
                    .SetProperty(n => n.LidaEm, agora), ct);
        }

        // #2303: mesmo desenho do caixa. Parte dos avisos NÃO LIDOS de cliente sem resposta e pergunta quais lembretes
        // ainda estão abertos; o resto (concluído, cancelado ou apagado) sai do sino.
        public async Task<int> ResolverAvisosDeClienteAtendidoAsync(Guid? empresaId = null, CancellationToken ct = default)
        {
            var prefixoPrazo = PrazoEstouradoEvento.PrefixoCorrelationId(TipoPrazo.ClienteSemResposta);

            var avisos = await (
                from n in dbContext.Notificacoes.IgnoreQueryFilters().AsNoTracking()
                join o in dbContext.NotifOutboxMensagens.IgnoreQueryFilters() on n.OutboxMensagemId equals (Guid?)o.Id
                join e in dbContext.NotifEventos.IgnoreQueryFilters() on o.EventoId equals e.Id
                where !n.Lida && (empresaId == null || n.EmpresaId == empresaId)
                      && e.Tipo == TipoEventoNotificacao.PrazoEstourado && e.CorrelationId.StartsWith(prefixoPrazo)
                      && e.RefEntidadeId != null
                select new { AvisoId = n.Id, n.EmpresaId, LembreteId = e.RefEntidadeId!.Value })
                .ToListAsync(ct);
            if (avisos.Count == 0) return 0;

            var lembreteIds = avisos.Select(a => a.LembreteId).Distinct().ToList();
            var abertos = (await dbContext.Lembretes.IgnoreQueryFilters().AsNoTracking()
                    .Where(l => lembreteIds.Contains(l.Id) && l.Situacao == SituacaoLembrete.Aberto)
                    .Select(l => new { l.Id, l.EmpresaId })
                    .ToListAsync(ct))
                .Select(l => (l.Id, l.EmpresaId))
                .ToHashSet();

            var resolver = avisos.Where(a => !abertos.Contains((a.LembreteId, a.EmpresaId))).Select(a => a.AvisoId).ToList();
            if (resolver.Count == 0) return 0;

            var agora = DateTime.UtcNow;
            return await dbContext.Notificacoes.IgnoreQueryFilters()
                .Where(n => resolver.Contains(n.Id) && !n.Lida)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(n => n.Lida, true)
                    .SetProperty(n => n.LidaEm, agora), ct);
        }

        private static Guid? LerAberturaId(string payloadJson)
        {
            try
            {
                using var doc = JsonDocument.Parse(payloadJson);
                return doc.RootElement.ValueKind == JsonValueKind.Object
                       && doc.RootElement.TryGetProperty("aberturaId", out var v)
                       && v.ValueKind == JsonValueKind.String
                       && Guid.TryParse(v.GetString(), out var id)
                    ? id
                    : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        public Task<int> CountNaoLidasAsync(Guid empresaId, Guid? usuarioId = null) =>
            dbContext.Notificacoes.CountAsync(n => n.EmpresaId == empresaId && !n.Lida
                && (!usuarioId.HasValue || n.UsuarioId == null || n.UsuarioId == usuarioId));

        public Task AddAsync(Notificacao notificacao) =>
            dbContext.Notificacoes.AddAsync(notificacao).AsTask();

        public Task UpdateAsync(Notificacao notificacao)
        {
            dbContext.Notificacoes.Update(notificacao);
            return Task.CompletedTask;
        }

        public async Task<IReadOnlyList<Guid>> MarcarTodasComoLidasAsync(Guid empresaId, Guid? usuarioId = null)
        {
            // #3400: o instante cortado no microssegundo do Postgres marca esta leitura; os ids saem por ele depois da
            // gravação, então um aviso que um job ou colega leu no meio não entra no Desfazer.
            var agora = DateTime.UtcNow;
            agora = agora.AddTicks(-(agora.Ticks % 10));
            var doEscopo = dbContext.Notificacoes
                .Where(n => n.EmpresaId == empresaId && (!usuarioId.HasValue || n.UsuarioId == null || n.UsuarioId == usuarioId));
            await doEscopo.Where(n => !n.Lida)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(n => n.Lida, true)
                    .SetProperty(n => n.LidaEm, agora));
            return await doEscopo.Where(n => n.Lida && n.LidaEm == agora).Select(n => n.Id).ToListAsync();
        }

        public Task<int> MarcarComoNaoLidasAsync(Guid empresaId, IReadOnlyCollection<Guid> ids, Guid usuarioId) =>
            dbContext.Notificacoes
                .Where(n => n.EmpresaId == empresaId && n.Lida && ids.Contains(n.Id) && (n.UsuarioId == null || n.UsuarioId == usuarioId))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(n => n.Lida, false)
                    .SetProperty(n => n.LidaEm, (DateTime?)null));

        public async Task DeleteAsync(Guid empresaId, Guid id)
        {
            // Defesa multi-tenant: DELETE condicional via ExecuteDeleteAsync.
            await dbContext.Notificacoes
                .Where(n => n.Id == id && n.EmpresaId == empresaId)
                .ExecuteDeleteAsync();
        }
    }
}
