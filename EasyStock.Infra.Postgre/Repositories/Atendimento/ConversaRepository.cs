using EasyStock.Application.Ports.Output.Persistence.Atendimento;
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
    private const int MaxMensagens = 500;
    private const int MaxPagina = 200;

    public Task<Conversa?> ObterPorIdAsync(Guid empresaId, Guid id, CancellationToken ct = default) =>
        db.AtendimentoConversas.FirstOrDefaultAsync(c => c.EmpresaId == empresaId && c.Id == id, ct);

    public Task<Conversa?> ObterAbertaPorContatoAsync(Guid empresaId, CanalConversa canal, string contatoIdExterno, CancellationToken ct = default)
    {
        var contato = Conversa.NormalizarContato(canal, contatoIdExterno);
        return db.AtendimentoConversas.FirstOrDefaultAsync(
            c => c.EmpresaId == empresaId
                 && c.Canal == canal
                 && c.ContatoIdExterno == contato
                 && c.Situacao != SituacaoConversa.Encerrada,
            ct);
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

    public async Task<IReadOnlyList<Conversa>> ListarPorClienteAsync(Guid empresaId, Guid clienteId, int max = 5, CancellationToken ct = default) =>
        await db.AtendimentoConversas
            .AsNoTracking()
            .Where(c => c.EmpresaId == empresaId && c.ClienteId == clienteId)
            .OrderByDescending(c => c.UltimaMensagemEm)
            .Take(Math.Clamp(max, 1, MaxPagina))
            .ToListAsync(ct);

    public Task<Mensagem?> ObterMensagemPorExternoIdAsync(Guid empresaId, string externoId, CancellationToken ct = default) =>
        db.AtendimentoMensagens.FirstOrDefaultAsync(m => m.EmpresaId == empresaId && m.ExternoId == externoId, ct);

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
