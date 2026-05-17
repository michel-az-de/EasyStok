using System.Runtime.CompilerServices;
using System.Text.Json;
using EasyStock.Application.Reporting;
using EasyStock.Application.Reporting.Definitions.Admin.Tenants;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Reporting;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Async.Reporting.Handlers.Admin;

/// <summary>
/// Handler do relatório de uso de tenants — Admin SaaS.
/// Visão geral cross-tenant com plano, status e métricas de uso.
/// </summary>
public sealed class TenantsUsoHandler(EasyStockDbContext db)
    : IReportHandler<TenantsUsoParams, TenantsUsoRow>
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public ReportSchema GetSchema(TenantsUsoParams parametros)
    {
        return new ReportSchema(
            title:        "Uso de tenants",
            fileNameBase: "tenants-uso",
            columns:
            [
                new("EmpresaId",              "ID empresa",               0),
                new("EmpresaNome",            "Empresa",                  1),
                new("EmpresaDocumento",       "CNPJ/CPF",                 2),
                new("Plano",                  "Plano",                    3),
                new("StatusAssinatura",       "Status assinatura",        4),
                new("DataCadastro",           "Cadastrado em",            5, "dd/MM/yyyy"),
                new("DataUltimoLogin",        "Último login",             6, "dd/MM/yyyy HH:mm"),
                new("TotalUsuarios",          "Usuários",                 7),
                new("TotalLojas",             "Lojas",                    8),
                new("TotalVendas30Dias",      "Vendas (30 dias)",         9),
                new("ReceitaUltimos30Dias",   "Receita 30 dias (R$)",    10, "0.00"),
                new("DataVencimentoAssinatura","Vencimento assinatura",  11, "dd/MM/yyyy"),
            ]);
    }

    public Task ValidateAsync(TenantsUsoParams parametros, CancellationToken ct) =>
        Task.CompletedTask;

    public async IAsyncEnumerable<TenantsUsoRow> StreamAsync(
        TenantsUsoParams parametros,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var limite30Dias = DateTime.UtcNow.AddDays(-30);

        var empresasQuery = db.Empresas
            .IgnoreQueryFilters()
            .OrderBy(e => e.Nome)
            .AsNoTracking();

        IQueryable<EasyStock.Domain.Entities.Empresa> empresasFiltradas = empresasQuery;
        if (parametros.StatusAssinatura is { Length: > 0 } statusFiltro)
        {
            if (Enum.TryParse<StatusAssinatura>(statusFiltro, ignoreCase: true, out var statusEnum))
            {
                empresasFiltradas = empresasQuery.Where(e =>
                    db.AssinaturasEmpresa
                        .IgnoreQueryFilters()
                        .Any(a => a.EmpresaId == e.Id && a.Status == statusEnum));
            }
        }

        await foreach (var empresa in empresasFiltradas.AsAsyncEnumerable().WithCancellation(ct))
        {
            // Assinatura mais recente
            var assinatura = await db.AssinaturasEmpresa
                .IgnoreQueryFilters()
                .Where(a => a.EmpresaId == empresa.Id)
                .Include(a => a.Plano)
                .OrderByDescending(a => a.DataInicio)
                .FirstOrDefaultAsync(ct);

            if (parametros.Plano is { Length: > 0 } planoFiltro)
            {
                if (assinatura?.Plano?.Nome?.Equals(planoFiltro, StringComparison.OrdinalIgnoreCase) == false)
                    continue;
            }

            var totalUsuarios = await db.UsuariosEmpresas
                .IgnoreQueryFilters()
                .CountAsync(ue => ue.EmpresaId == empresa.Id, ct);

            var totalLojas = await db.Lojas
                .IgnoreQueryFilters()
                .CountAsync(l => l.EmpresaId == empresa.Id, ct);

            var totalVendas30Dias = await db.Vendas
                .IgnoreQueryFilters()
                .CountAsync(v => v.EmpresaId == empresa.Id && v.DataVenda >= limite30Dias, ct);

            var receita30Dias = await db.Vendas
                .IgnoreQueryFilters()
                .Where(v => v.EmpresaId == empresa.Id && v.DataVenda >= limite30Dias)
                .SumAsync(v => (decimal?)v.ValorTotal.Valor, ct) ?? 0m;

            yield return new TenantsUsoRow(
                EmpresaId:                empresa.Id,
                EmpresaNome:              empresa.Nome,
                EmpresaDocumento:         empresa.Documento ?? "—",
                Plano:                    assinatura?.Plano?.Nome ?? "—",
                StatusAssinatura:         assinatura?.Status.ToString() ?? "Sem assinatura",
                DataCadastro:             empresa.CriadoEm,
                DataUltimoLogin:          null, // Empresa não possui UltimoLoginEm
                TotalUsuarios:            totalUsuarios,
                TotalLojas:               totalLojas,
                TotalVendas30Dias:        totalVendas30Dias,
                ReceitaUltimos30Dias:     receita30Dias,
                DataVencimentoAssinatura: assinatura?.DataFim);
        }
    }

    public TenantsUsoParams DeserializeParams(string paramsJson) =>
        JsonSerializer.Deserialize<TenantsUsoParams>(paramsJson, _jsonOptions)
            ?? throw new InvalidOperationException("Falha ao deserializar TenantsUsoParams.");
}
