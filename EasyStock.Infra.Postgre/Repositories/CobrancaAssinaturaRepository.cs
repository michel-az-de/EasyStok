using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.Repositories;

public sealed class CobrancaAssinaturaRepository(EasyStockDbContext dbContext) : ICobrancaAssinaturaRepository
{
    public Task AddAsync(CobrancaAssinatura cobranca) =>
        dbContext.CobrancasAssinatura.AddAsync(cobranca).AsTask();

    public Task UpdateAsync(CobrancaAssinatura cobranca)
    {
        dbContext.CobrancasAssinatura.Update(cobranca);
        return Task.CompletedTask;
    }

    public Task<CobrancaAssinatura?> GetByTxidAsync(string txid) =>
        dbContext.CobrancasAssinatura
            .FirstOrDefaultAsync(c => c.Txid == txid);

    public Task<bool> ExistePendenteAsync(Guid empresaId) =>
        dbContext.CobrancasAssinatura
            .AnyAsync(c => c.EmpresaId == empresaId && c.Status == StatusCobranca.Pendente);
}
