using EasyStock.Application.Ports.Output.Integration.Logistica;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.UseCases.Atendimento.Entregas;

// I9.1 (#1669, ADR-0061 D5): corrida pedida por fora, registrada à mão com custo. Arquivo próprio: não
// toca SairParaEntregaUseCase nem as viagens; a viagem manual segue saindo com o entregador genérico do
// provedor (RN-32 intacta).

/// <summary>Corrida da viagem. Os campos depois de <c>CorrigidoEm</c> só vêm na corrida chamada pela API (#2162).</summary>
public sealed record CorridaExternaResult(
    Guid Id, Guid ViagemId, EmpresaEntregador Provedor, OrigemCorridaExterna Origem, int CustoCentavos, string? Motorista,
    Guid RegistradoPorUsuarioId, DateTime RegistradoEm, Guid? CorrigidoPorUsuarioId, DateTime? CorrigidoEm,
    StatusCorrida? Status = null, VeiculoCorrida? Veiculo = null, int? CustoCotadoCentavos = null, string? LinkRastreio = null,
    string? MotoristaTelefone = null, string? MotoristaPlaca = null, bool Ativa = false, bool PodeCancelar = false)
{
    internal static CorridaExternaResult De(CorridaExterna c, DateTime? agora = null) => new(
        c.Id, c.ViagemId, c.Provedor, c.Origem, c.CustoCentavos, c.Motorista,
        c.RegistradoPorUsuarioId, c.RegistradoEm, c.CorrigidoPorUsuarioId, c.CorrigidoEm,
        c.Status, c.Veiculo, c.CustoCotadoCentavos, c.LinkRastreio, c.MotoristaTelefone, c.MotoristaPlaca, c.Ativa,
        agora is { } instante && c.PodeCancelar(instante));
}

public sealed record RegistrarCorridaExternaInput(
    Guid EmpresaId, Guid UsuarioId, Guid ViagemId, EmpresaEntregador Provedor, int CustoCentavos, string? Motorista);

public sealed record CorrigirCorridaExternaInput(
    Guid EmpresaId, Guid UsuarioId, Guid ViagemId, Guid CorridaId, int CustoCentavos, string? Motorista);

/// <summary>Corrida inexistente, de outra loja ou de outra viagem: 404, sem vazar existência.</summary>
public sealed class CorridaExternaNaoEncontradaException(Guid id) : Exception($"Corrida {id} não encontrada.");

public sealed class RegistrarCorridaExternaUseCase(
    IViagemRepository viagens, ICorridaExternaRepository corridas, IUnitOfWork unitOfWork, TimeProvider relogio)
{
    public async Task<CorridaExternaResult> ExecuteAsync(RegistrarCorridaExternaInput input, CancellationToken ct = default)
    {
        var viagem = await viagens.ObterAsync(input.EmpresaId, input.ViagemId, ct) ?? throw new ViagemNaoEncontradaException(input.ViagemId);
        var corrida = Regra.Validar(() => CorridaExterna.RegistrarManual(
            viagem, input.Provedor, input.CustoCentavos, input.Motorista, input.UsuarioId, relogio.GetUtcNow().UtcDateTime));
        await corridas.AddAsync(corrida, ct);
        await unitOfWork.CommitAsync();
        return CorridaExternaResult.De(corrida);
    }
}

public sealed class CorrigirCorridaExternaUseCase(
    IViagemRepository viagens, ICorridaExternaRepository corridas, IUnitOfWork unitOfWork, TimeProvider relogio)
{
    public async Task<CorridaExternaResult> ExecuteAsync(CorrigirCorridaExternaInput input, CancellationToken ct = default)
    {
        var viagem = await viagens.ObterAsync(input.EmpresaId, input.ViagemId, ct) ?? throw new ViagemNaoEncontradaException(input.ViagemId);
        var corrida = await corridas.ObterAsync(input.EmpresaId, input.CorridaId, ct);
        if (corrida is null || corrida.ViagemId != viagem.Id) throw new CorridaExternaNaoEncontradaException(input.CorridaId);

        Regra.Validar(() =>
        {
            corrida.Corrigir(viagem, input.CustoCentavos, input.Motorista, input.UsuarioId, relogio.GetUtcNow().UtcDateTime);
            return corrida;
        });
        await unitOfWork.CommitAsync();
        return CorridaExternaResult.De(corrida);
    }
}

public sealed class ListarCorridasDaViagemUseCase(IViagemRepository viagens, ICorridaExternaRepository corridas, TimeProvider relogio)
{
    public async Task<IReadOnlyList<CorridaExternaResult>> ExecuteAsync(Guid empresaId, Guid viagemId, CancellationToken ct = default)
    {
        _ = await viagens.ObterAsync(empresaId, viagemId, ct) ?? throw new ViagemNaoEncontradaException(viagemId);
        var agora = relogio.GetUtcNow().UtcDateTime;
        return (await corridas.ListarDaViagemAsync(empresaId, viagemId, ct)).Select(c => CorridaExternaResult.De(c, agora)).ToList();
    }
}

/// <summary>#2162: o console só oferece "Chamar Lalamove" com a loja conectada. Nunca diz qual chave.</summary>
public sealed class ConexaoDespachoUseCase(ILogisticaGateway gateway)
{
    public sealed record Resultado(bool LalamoveConectada);

    public async Task<Resultado> ExecuteAsync(Guid empresaId, CancellationToken ct = default) =>
        new(await gateway.ConectadoAsync(empresaId, ct));
}

public sealed record CustoDeEntregaViagem(Guid ViagemId, DateTime CriadaEm, int Paradas, int FreteCobradoCentavos, int CustoCentavos)
{
    /// <summary>Frete cobrado menos custo da corrida: negativo é entrega que custou mais do que rendeu.</summary>
    public int SaldoCentavos => FreteCobradoCentavos - CustoCentavos;
}

public sealed record CustoDeEntregaResult(
    IReadOnlyList<CustoDeEntregaViagem> Viagens, int FreteCobradoCentavos, int CustoCentavos)
{
    public int SaldoCentavos => FreteCobradoCentavos - CustoCentavos;

    public static CustoDeEntregaResult De(IEnumerable<CustoDeEntregaLinha> linhas)
    {
        var viagens = linhas.OrderBy(l => l.CriadaEm)
            .Select(l => new CustoDeEntregaViagem(l.ViagemId, l.CriadaEm, l.Paradas, l.FreteCobradoCentavos, l.CustoCentavos))
            .ToList();
        return new CustoDeEntregaResult(viagens, viagens.Sum(v => v.FreteCobradoCentavos), viagens.Sum(v => v.CustoCentavos));
    }
}

/// <summary>I9.1: custo das corridas contra o frete cobrado, por viagem criada em [de, ate). No máximo 93 dias.</summary>
public sealed class CustoDeEntregaUseCase(ICustoDeEntregaQuery query)
{
    public const int JanelaMaximaDias = 93;

    public async Task<CustoDeEntregaResult> ExecuteAsync(Guid empresaId, DateTime de, DateTime ate, CancellationToken ct = default)
    {
        if (ate <= de) throw new UseCaseValidationException("O fim do período deve ser depois do início.");
        if ((ate - de).TotalDays > JanelaMaximaDias)
            throw new UseCaseValidationException($"Período de no máximo {JanelaMaximaDias} dias.");
        return CustoDeEntregaResult.De(await query.ListarAsync(empresaId, de, ate, ct));
    }
}