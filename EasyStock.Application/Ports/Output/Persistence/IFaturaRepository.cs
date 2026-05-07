using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;

namespace EasyStock.Application.Ports.Output.Persistence;

/// <summary>
/// Repositorio do agregado <see cref="Fatura"/>. Todas as queries respeitam
/// multi-tenancy via <see cref="EmpresaId"/> obrigatorio (exceto admin global).
/// </summary>
public interface IFaturaRepository
{
    Task AddAsync(Fatura fatura, CancellationToken ct = default);
    Task UpdateAsync(Fatura fatura, CancellationToken ct = default);

    /// <summary>Carrega Fatura com Itens, Pagamentos e Eventos (eager). Filtra por EmpresaId.</summary>
    Task<Fatura?> GetByIdAsync(Guid empresaId, Guid faturaId, CancellationToken ct = default);

    /// <summary>Versao admin (sem filtro de EmpresaId). Usar apenas em controllers admin com permissao.</summary>
    Task<Fatura?> GetByIdAdminAsync(Guid faturaId, CancellationToken ct = default);

    /// <summary>Lista paginada para o cliente — filtra por EmpresaId.</summary>
    Task<(IReadOnlyList<Fatura> Itens, int Total)> ListarClienteAsync(
        Guid empresaId,
        StatusFatura? status = null,
        DateTime? vencimentoDe = null,
        DateTime? vencimentoAte = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default);

    /// <summary>Lista paginada admin — filtros opcionais por empresa, status, etc.</summary>
    Task<(IReadOnlyList<Fatura> Itens, int Total)> ListarAdminAsync(
        Guid? empresaId = null,
        StatusFatura? status = null,
        OrigemFatura? origem = null,
        DateTime? vencimentoDe = null,
        DateTime? vencimentoAte = null,
        decimal? valorMin = null,
        decimal? valorMax = null,
        string? busca = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default);

    /// <summary>Busca por origem (ex: encontrar fatura existente para uma assinatura).</summary>
    Task<Fatura?> GetByOrigemAsync(Guid empresaId, OrigemFatura origem, Guid origemRefId, CancellationToken ct = default);
}
