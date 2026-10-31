using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.Ports.Output.Persistence.Atendimento;

/// <summary>Corridas externas de uma viagem (I9.1). Toda leitura leva a empresa do token.</summary>
public interface ICorridaExternaRepository
{
    Task AddAsync(CorridaExterna corrida, CancellationToken ct = default);
    Task<CorridaExterna?> ObterAsync(Guid empresaId, Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<CorridaExterna>> ListarDaViagemAsync(Guid empresaId, Guid viagemId, CancellationToken ct = default);

    /// <summary>
    /// Webhook (#2160): o provedor só manda o id do pedido dele. Cruza lojas de propósito (sem o RLS) e devolve só
    /// a loja e a corrida; a leitura da corrida em si segue pelo tenant.
    /// </summary>
    Task<CorridaLocalizada?> LocalizarPorIdExternoAsync(EmpresaEntregador provedor, string idExterno, CancellationToken ct = default);

    /// <summary>Consulta de contingência (R6): corridas da API em curso em todas as lojas, a mais antiga primeiro.</summary>
    Task<IReadOnlyList<CorridaLocalizada>> ListarEmCursoAsync(int maximo, CancellationToken ct = default);
}

public sealed record CorridaLocalizada(Guid EmpresaId, Guid CorridaId);

/// <summary>Uma viagem no relatório de custo de entrega: o que foi cobrado de frete e o que a corrida custou.</summary>
public sealed record CustoDeEntregaLinha(Guid ViagemId, DateTime CriadaEm, int Paradas, int FreteCobradoCentavos, int CustoCentavos);

/// <summary>Leitura do relatório de custo contra frete (I9.1). Viagem desfeita só entra se tiver corrida registrada.</summary>
public interface ICustoDeEntregaQuery
{
    Task<IReadOnlyList<CustoDeEntregaLinha>> ListarAsync(Guid empresaId, DateTime de, DateTime ate, CancellationToken ct = default);
}