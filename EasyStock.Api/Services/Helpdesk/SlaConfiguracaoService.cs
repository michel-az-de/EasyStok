using EasyStock.Application.Ports.Output;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Api.Services.Helpdesk;

public sealed record SlaConfigItemView(
    Guid Id,
    Guid? EmpresaId,
    Guid? PlanoId,
    string Prioridade,
    int MinutosResposta,
    int MinutosResolucao);

public sealed class SlaConfiguracaoService(
    EasyStockDbContext db,
    ICurrentUserAccessor currentUser)
{
    public async Task<IReadOnlyList<SlaConfigItemView>> ListarAsync(CancellationToken ct = default)
    {
        var lista = await db.SlaConfiguracoes
            .AsNoTracking()
            .OrderBy(s => s.PlanoId)
            .ThenBy(s => s.EmpresaId)
            .ThenBy(s => s.Prioridade)
            .Select(s => new SlaConfigItemView(s.Id, s.EmpresaId, s.PlanoId, s.Prioridade.ToString(), s.MinutosResposta, s.MinutosResolucao))
            .ToListAsync(ct);
        return lista;
    }

    public async Task SalvarLoteAsync(IReadOnlyList<SalvarSlaConfigItem> itens, CancellationToken ct = default)
    {
        if (!currentUser.TemPermissao(Permissao.ConfigurarSla))
            throw new UnauthorizedAccessException("Sem permissao para configurar SLA.");

        if (itens is null || itens.Count == 0) return;

        foreach (var item in itens)
        {
            if (item.MinutosResposta <= 0 || item.MinutosResolucao <= 0)
                throw new InvalidOperationException("Minutos de SLA precisam ser positivos.");
            if (item.MinutosResposta > item.MinutosResolucao)
                throw new InvalidOperationException("Tempo de resposta nao pode ser maior que tempo de resolucao.");

            var existente = await db.SlaConfiguracoes
                .FirstOrDefaultAsync(s => s.EmpresaId == item.EmpresaId && s.PlanoId == item.PlanoId && s.Prioridade == item.Prioridade, ct);

            if (existente is null)
            {
                db.SlaConfiguracoes.Add(SlaConfiguracao.Criar(
                    item.Prioridade, item.MinutosResposta, item.MinutosResolucao,
                    empresaId: item.EmpresaId, planoId: item.PlanoId));
            }
            else
            {
                existente.MinutosResposta = item.MinutosResposta;
                existente.MinutosResolucao = item.MinutosResolucao;
                existente.AlteradoEm = DateTime.UtcNow;
            }
        }

        await db.CommitAsync();
    }
}
