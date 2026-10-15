using System.Text.Json;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;

namespace EasyStock.Application.UseCases.Atendimento.Entregas;

public sealed record RastreioLogisticaEvento(string Tipo, DateTime Em);
public sealed record RastreioLogisticaResult(Guid Id, string Status, bool Demonstracao,
    DateTime AtualizadoEm, IReadOnlyList<RastreioLogisticaEvento> Eventos);

/// <summary>Projeção pública mínima. A API valida o token e estabelece o tenant antes da consulta.</summary>
public sealed class ObterRastreioLogisticaUseCase(ILogisticaRepository repository, ConfiguracaoLogistica configuracao)
{
    public async Task<RastreioLogisticaResult?> ExecuteAsync(Guid empresaId, Guid despachoId, CancellationToken ct = default)
    {
        if (empresaId == Guid.Empty || empresaId != configuracao.EmpresaDemonstracaoId || despachoId == Guid.Empty)
            return null;
        var despacho = await repository.DespachoAsync(empresaId, despachoId, ct);
        if (despacho is null || despacho.EmpresaId != empresaId) return null;
        var eventos = (JsonSerializer.Deserialize<List<LogisticaEvento>>(despacho.EventosJson) ?? [])
            .Where(e => e.Tipo is "agendado" or "a_caminho" or "entregue" or "falha" or "devolvendo" or "devolvido" or "cancelado")
            .OrderBy(e => e.Em)
            .Select(e => new RastreioLogisticaEvento(e.Tipo, e.Em)).ToList();
        return new(despacho.Id, despacho.Status, true, eventos.LastOrDefault()?.Em ?? despacho.CriadoEm, eventos);
    }
}
