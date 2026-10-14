using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.UseCases.Atendimento.Entregas;

// I9.1 (#1669, ADR-0061 D5): corrida pedida por fora, registrada à mão com custo. Arquivo próprio: não
// toca SairParaEntregaUseCase nem as viagens; a viagem manual segue saindo com o entregador genérico do
// provedor (RN-32 intacta).

public sealed record CorridaExternaResult(
    Guid Id, Guid ViagemId, EmpresaEntregador Provedor, OrigemCorridaExterna Origem, int CustoCentavos, string? Motorista,
    Guid RegistradoPorUsuarioId, DateTime RegistradoEm, Guid? CorrigidoPorUsuarioId, DateTime? CorrigidoEm)
{
    internal static CorridaExternaResult De(CorridaExterna c) => new(
        c.Id, c.ViagemId, c.Provedor, c.Origem, c.CustoCentavos, c.Motorista,
        c.RegistradoPorUsuarioId, c.RegistradoEm, c.CorrigidoPorUsuarioId, c.CorrigidoEm);
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

public sealed class ListarCorridasDaViagemUseCase(IViagemRepository viagens, ICorridaExternaRepository corridas)
{
    public async Task<IReadOnlyList<CorridaExternaResult>> ExecuteAsync(Guid empresaId, Guid viagemId, CancellationToken ct = default)
    {
        _ = await viagens.ObterAsync(empresaId, viagemId, ct) ?? throw new ViagemNaoEncontradaException(viagemId);
        return (await corridas.ListarDaViagemAsync(empresaId, viagemId, ct)).Select(CorridaExternaResult.De).ToList();
    }
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