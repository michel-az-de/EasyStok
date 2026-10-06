using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories.Atendimento;

/// <summary>
/// Persistencia de Conversa/Mensagem (S04). <c>empresaId</c> vai no WHERE de toda
/// consulta alem do filtro global do DbContext e do RLS (ADR-0010).
/// </summary>
public sealed class ConversaRepository(EasyStockDbContext db) : IConversaRepository
{
    public Task<Conversa?> ObterAbertaPorClienteNoCanalAsync(Guid empresaId, Guid clienteId, CanalConversa canal, CancellationToken ct = default) =>
        db.AtendimentoConversas.Where(c => c.EmpresaId == empresaId && c.ClienteId == clienteId
            && c.Canal == canal && c.Situacao != SituacaoConversa.Encerrada)
            .OrderByDescending(c => c.IniciadaEm).FirstOrDefaultAsync(ct);

    public Task TravarContatoAsync(Guid empresaId, string contato, CancellationToken ct = default)
    {
        var chave = $"chat-site:{empresaId:N}:{contato}";
        return db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({chave}))", ct);
    }

    private const int MaxMensagens = 500;
    private const int MaxPagina = 200;

    public Task<Conversa?> ObterPorIdAsync(Guid empresaId, Guid id, CancellationToken ct = default) =>
        db.AtendimentoConversas.FirstOrDefaultAsync(c => c.EmpresaId == empresaId && c.Id == id, ct);

    public async Task<Guid?> TravarParaPedidoAsync(Guid empresaId, Guid id, CancellationToken ct = default)
    {
        // Leitura escalar de propósito: a entidade já rastreada no escopo (agente, console) não é
        // relida pelo EF, e recarregá-la descartaria mudanças pendentes. Só esta coluna decide.
        var linhas = await db.Database
            .SqlQuery<Guid?>($"""
                SELECT "PedidoEmAndamentoId" AS "Value"
                FROM atendimento_conversas
                WHERE "EmpresaId" = {empresaId} AND "Id" = {id}
                FOR UPDATE
                """)
            .ToListAsync(ct);
        return linhas.Count == 0 ? null : linhas[0];
    }

    public Task<SituacaoConversa?> ObterSituacaoAsync(Guid empresaId, Guid id, CancellationToken ct = default) =>
        db.AtendimentoConversas
            .AsNoTracking()
            .Where(c => c.EmpresaId == empresaId && c.Id == id)
            .Select(c => (SituacaoConversa?)c.Situacao)
            .FirstOrDefaultAsync(ct);

    public Task<Conversa?> ObterAbertaPorContatoAsync(Guid empresaId, CanalConversa canal, string contatoIdExterno, CancellationToken ct = default)
    {
        var contato = Conversa.NormalizarContato(canal, contatoIdExterno);
        // #1290: no WhatsApp o mesmo celular aparece com e sem o nono dígito (wa_id da Meta x cadastro).
        // string[] de propósito: o Npgsql manda array como parâmetro (= ANY), sem tipo gerado pelo compilador.
        string[] grafias = canal == CanalConversa.WhatsApp
            ? [.. NormalizadorTelefone.VariantesContatoWhatsApp(contato)]
            : [contato];
        return db.AtendimentoConversas
            .Where(c => c.EmpresaId == empresaId
                        && c.Canal == canal
                        && grafias.Contains(c.ContatoIdExterno)
                        && c.Situacao != SituacaoConversa.Encerrada)
            .OrderBy(c => c.ContatoIdExterno == contato ? 0 : 1)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<ConversaComMensagens?> ObterComMensagensAsync(Guid empresaId, Guid id, int ultimasN, CancellationToken ct = default)
    {
        var conversa = await ObterPorIdAsync(empresaId, id, ct);
        if (conversa is null) return null;

        var take = Math.Clamp(ultimasN, 1, MaxMensagens);
        var ultimas = await db.AtendimentoMensagens
            .AsNoTracking()
            .Where(m => m.EmpresaId == empresaId && m.ConversaId == id)
            .OrderByDescending(m => m.EnviadaEm)
            .ThenByDescending(m => m.Id)
            .Take(take)
            .ToListAsync(ct);

        ultimas.Reverse(); // cronologica: a mais nova por ultimo
        return new ConversaComMensagens(conversa, ultimas);
    }

    public async Task<IReadOnlyList<Conversa>> ListarAsync(
        Guid empresaId,
        SituacaoConversa? situacao,
        int pagina,
        int tamanhoPagina,
        CancellationToken ct = default)
    {
        var paginaEfetiva = Math.Max(pagina, 1);
        var tamanho = Math.Clamp(tamanhoPagina, 1, MaxPagina);

        var query = db.AtendimentoConversas
            .AsNoTracking()
            .Where(c => c.EmpresaId == empresaId);

        if (situacao is { } s)
            query = query.Where(c => c.Situacao == s);

        return await query
            .OrderByDescending(c => c.UltimaMensagemEm)
            .Skip((paginaEfetiva - 1) * tamanho)
            .Take(tamanho)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ConversaInboxItem>> ListarInboxAsync(
        Guid empresaId,
        SituacaoConversa? situacao,
        string? busca,
        FiltroResponsavel? responsavel,
        int pagina,
        int tamanhoPagina,
        CancellationToken ct = default)
    {
        var paginaEfetiva = Math.Max(pagina, 1);
        var tamanho = Math.Clamp(tamanhoPagina, 1, MaxPagina);

        var query = db.AtendimentoConversas
            .AsNoTracking()
            .Where(c => c.EmpresaId == empresaId);

        if (situacao is { } s)
            query = query.Where(c => c.Situacao == s);

        if (responsavel is not null)
            query = query.Where(c => c.AssumidaPorUsuarioId == responsavel.UsuarioId);

        if (!string.IsNullOrWhiteSpace(busca))
        {
            var padrao = $"%{busca.Trim()}%";
            query = query.Where(c => EF.Functions.ILike(c.ContatoNome ?? string.Empty, padrao)
                                     || EF.Functions.ILike(c.ContatoIdExterno, padrao));
        }

        var linhas = await query
            .OrderByDescending(c => c.UltimaMensagemEm)
            .Skip((paginaEfetiva - 1) * tamanho)
            .Take(tamanho)
            .Select(c => new
            {
                Conversa = c,
                UltimoTexto = db.AtendimentoMensagens
                    .Where(m => m.EmpresaId == empresaId && m.ConversaId == c.Id)
                    .OrderByDescending(m => m.EnviadaEm)
                    .ThenByDescending(m => m.Id)
                    .Select(m => m.Texto)
                    .FirstOrDefault(),
            })
            .ToListAsync(ct);

        return linhas.Select(l => new ConversaInboxItem(l.Conversa, l.UltimoTexto)).ToList();
    }

    public async Task<IReadOnlyList<Mensagem>> ListarMensagensAsync(
        Guid empresaId,
        Guid conversaId,
        DateTime? antesDe,
        int limite,
        CancellationToken ct = default)
    {
        var query = db.AtendimentoMensagens
            .AsNoTracking()
            .Where(m => m.EmpresaId == empresaId && m.ConversaId == conversaId);

        if (antesDe is { } cursor) // UTC: o use case normaliza
            query = query.Where(m => m.EnviadaEm < cursor);

        var pagina = await query
            .OrderByDescending(m => m.EnviadaEm)
            .ThenByDescending(m => m.Id)
            .Take(Math.Clamp(limite, 1, MaxMensagens))
            .ToListAsync(ct);

        pagina.Reverse(); // cronologica: a mais nova por ultimo
        return pagina;
    }

    public async Task<IReadOnlyList<Mensagem>> ListarMensagensDepoisAsync(
        Guid empresaId,
        Guid conversaId,
        DateTime? depoisDe,
        int limite,
        CancellationToken ct = default)
    {
        var query = db.AtendimentoMensagens
            .AsNoTracking()
            .Where(m => m.EmpresaId == empresaId && m.ConversaId == conversaId);

        if (depoisDe is { } cursor) // UTC: o use case normaliza
            query = query.Where(m => m.EnviadaEm > cursor);

        return await query
            .OrderBy(m => m.EnviadaEm)
            .ThenBy(m => m.Id)
            .Take(Math.Clamp(limite, 1, MaxMensagens))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Conversa>> ListarPorClienteAsync(Guid empresaId, Guid clienteId, int max = 5, CancellationToken ct = default) =>
        await db.AtendimentoConversas
            .AsNoTracking()
            .Where(c => c.EmpresaId == empresaId && c.ClienteId == clienteId)
            .OrderByDescending(c => c.UltimaMensagemEm)
            .Take(Math.Clamp(max, 1, MaxPagina))
            .ToListAsync(ct);

    public Task<Mensagem?> ObterMensagemPorExternoIdAsync(Guid empresaId, string externoId, CancellationToken ct = default) =>
        db.AtendimentoMensagens.FirstOrDefaultAsync(m => m.EmpresaId == empresaId && m.ExternoId == externoId, ct);

    public Task<Mensagem?> ObterMensagemAsync(Guid empresaId, Guid conversaId, Guid mensagemId, CancellationToken ct = default) =>
        db.AtendimentoMensagens.AsNoTracking().FirstOrDefaultAsync(
            m => m.EmpresaId == empresaId && m.ConversaId == conversaId && m.Id == mensagemId, ct);

    public Task<Mensagem?> ObterMensagemParaAlterarAsync(Guid empresaId, Guid conversaId, Guid mensagemId, CancellationToken ct = default) =>
        db.AtendimentoMensagens.FirstOrDefaultAsync(
            m => m.EmpresaId == empresaId && m.ConversaId == conversaId && m.Id == mensagemId, ct);

    // SQL cru de propósito: FOR UPDATE SKIP LOCKED não sai do LINQ. IgnoreQueryFilters mantém o SQL sem
    // composição; o serviço de reenvio é cross-tenant e roda com bypass de RLS ligado pelo host (S57).
    public async Task<IReadOnlyList<Mensagem>> ListarReenviosVencidosComLockAsync(DateTime agora, int limite, CancellationToken ct = default) =>
        await db.AtendimentoMensagens
            .FromSqlInterpolated($"""
                SELECT * FROM atendimento_mensagens
                WHERE "Status" = {(int)StatusMensagem.Falhou} AND "ProximoReenvioEm" <= {agora}
                ORDER BY "ProximoReenvioEm"
                LIMIT {limite}
                FOR UPDATE SKIP LOCKED
                """)
            .IgnoreQueryFilters()
            .ToListAsync(ct);

    // SQL cru: FOR UPDATE SKIP LOCKED não sai do LINQ. Cross-tenant, com bypass de RLS ligado pelo host (#1397).
    public async Task<IReadOnlyList<Mensagem>> ListarMidiasPendentesComLockAsync(DateTime agora, int limite, CancellationToken ct = default) =>
        await db.AtendimentoMensagens
            .FromSqlInterpolated($"""
                SELECT * FROM atendimento_mensagens
                WHERE "ProximaTentativaMidiaEm" <= {agora} AND "MidiaChave" IS NULL
                ORDER BY "ProximaTentativaMidiaEm"
                LIMIT {limite}
                FOR UPDATE SKIP LOCKED
                """)
            .IgnoreQueryFilters()
            .ToListAsync(ct);

    public Task<bool> ExisteAguardandoClienteAsync(
        Guid empresaId, CanalConversa canal, string contatoIdExterno, Guid? clienteId, DateTime desde, CancellationToken ct = default) =>
        db.AtendimentoMensagens
            .Where(m => m.EmpresaId == empresaId && m.Status == StatusMensagem.Falhou && m.AguardaClienteDesde > desde)
            .Join(db.AtendimentoConversas.Where(c => c.EmpresaId == empresaId && c.Canal == canal),
                m => m.ConversaId, c => c.Id, (m, c) => c)
            .AnyAsync(c => c.ContatoIdExterno == contatoIdExterno || (clienteId != null && c.ClienteId == clienteId), ct);

    // SQL cru: FOR UPDATE OF m SKIP LOCKED não sai do LINQ. Cross-tenant, com bypass de RLS ligado pelo host (S58).
    // O contato pode ter escrito numa conversa nova; o cliente vinculado cobre o celular com e sem o nono dígito.
    public async Task<IReadOnlyList<Mensagem>> ListarAguardandoComRespostaComLockAsync(int limite, CancellationToken ct = default) =>
        await db.AtendimentoMensagens
            .FromSqlInterpolated($"""
                SELECT m.* FROM atendimento_mensagens m
                JOIN atendimento_conversas c0 ON c0."Id" = m."ConversaId"
                WHERE m."Status" = {(int)StatusMensagem.Falhou} AND m."AguardaClienteDesde" IS NOT NULL
                  AND EXISTS (
                    SELECT 1 FROM atendimento_conversas c
                    WHERE c."EmpresaId" = c0."EmpresaId" AND c."Canal" = c0."Canal"
                      AND (c."ContatoIdExterno" = c0."ContatoIdExterno" OR (c0."ClienteId" IS NOT NULL AND c."ClienteId" = c0."ClienteId"))
                      AND c."UltimaMensagemEntradaEm" > m."AguardaClienteDesde")
                ORDER BY m."AguardaClienteDesde"
                LIMIT {limite}
                FOR UPDATE OF m SKIP LOCKED
                """)
            .IgnoreQueryFilters()
            .ToListAsync(ct);

    public async Task<IReadOnlyList<MensagemNaoEntregue>> ListarNaoEntreguesAsync(Guid empresaId, int limite, CancellationToken ct = default) =>
        await db.AtendimentoMensagens.AsNoTracking()
            .Where(m => m.EmpresaId == empresaId && m.Direcao == DirecaoMensagem.Saida && m.Status == StatusMensagem.Falhou)
            .OrderByDescending(m => m.EnviadaEm)
            .Take(Math.Clamp(limite, 1, MaxPagina))
            .Join(db.AtendimentoConversas.AsNoTracking().Where(c => c.EmpresaId == empresaId),
                m => m.ConversaId, c => c.Id, (m, c) => new MensagemNaoEntregue(c, m))
            .ToListAsync(ct);

    public Task AddAsync(Conversa conversa, CancellationToken ct = default)
    {
        db.AtendimentoConversas.Add(conversa);
        return Task.CompletedTask;
    }

    public Task AddMensagemAsync(Mensagem mensagem, CancellationToken ct = default)
    {
        db.AtendimentoMensagens.Add(mensagem);
        return Task.CompletedTask;
    }
}
