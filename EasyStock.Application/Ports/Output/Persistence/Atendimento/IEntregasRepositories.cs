using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.Ports.Output.Persistence.Atendimento;

/// <summary>Entregadores (S44). Consultas com <c>EmpresaId</c> no WHERE (ADR-0010).</summary>
public interface IEntregadorRepository
{
    Task AddAsync(Entregador entregador, CancellationToken ct = default);

    /// <summary>Rastreado.</summary>
    Task<Entregador?> ObterAsync(Guid empresaId, Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<Entregador>> ListarAsync(Guid empresaId, bool incluirInativos, CancellationToken ct = default);
}

/// <summary>Viagens de entrega (S44), sempre com as paradas.</summary>
public interface IViagemRepository
{
    Task AddAsync(Viagem viagem, CancellationToken ct = default);

    /// <summary>Rastreado, com as paradas.</summary>
    Task<Viagem?> ObterAsync(Guid empresaId, Guid id, CancellationToken ct = default);

    /// <summary>Mais recentes primeiro. Sem <paramref name="situacao"/>, todas.</summary>
    Task<IReadOnlyList<Viagem>> ListarAsync(Guid empresaId, SituacaoViagem? situacao, int limite, CancellationToken ct = default);

    /// <summary>O pedido já está numa viagem montando ou em rota?</summary>
    Task<bool> PedidoEmViagemAtivaAsync(Guid empresaId, Guid pedidoId, CancellationToken ct = default);

    /// <summary>
    /// #1440: marca como nova a parada que o domínio acabou de pôr numa viagem já gravada. O Id nasce no
    /// domínio; sem isto o EF a toma por existente e manda UPDATE de 0 linha (conflito de concorrência).
    /// </summary>
    Task RegistrarParadaNovaAsync(ParadaViagem parada, CancellationToken ct = default);
}

/// <summary>Chamados de entregador (S44).</summary>
public interface IChamadoEntregadorRepository
{
    Task AddAsync(ChamadoEntregador chamado, CancellationToken ct = default);

    /// <summary>Rastreado.</summary>
    Task<ChamadoEntregador?> ObterAsync(Guid empresaId, Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<ChamadoEntregador>> ListarAsync(Guid empresaId, bool apenasAbertos, int limite, CancellationToken ct = default);
}

/// <summary>Um pedido entregue no período, com o bairro do cliente e o total.</summary>
public sealed record PedidoEntregueLinha(string? Bairro, decimal Valor);

/// <summary>
/// Relatório de entregas por bairro (S44): pedidos com status entregue e <c>EntreguEm</c> em
/// [de, ate), do tenant. O bairro vem do cadastro do cliente até o endereço do pedido (S14) entrar.
/// </summary>
public interface IEntregasPorBairroQuery
{
    Task<IReadOnlyList<PedidoEntregueLinha>> ListarEntreguesAsync(Guid empresaId, DateTime de, DateTime ate, CancellationToken ct = default);
}
