using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.UseCases.Atendimento.Assistente;

public sealed record ListarHistoricoAssistenteQuery(
    Guid EmpresaId, Guid? ConversaId = null, DesfechoTurnoAgente? Desfecho = null, int Pagina = 1, int TamanhoPagina = 20);

/// <summary>S67 (#2534): o que o Assistente fez, turno a turno, para o painel da engrenagem.</summary>
public sealed class ListarHistoricoAssistenteUseCase(ITurnoAgenteRepository turnos)
{
    public const int TamanhoPaginaMaximo = 100;
    public const int PaginaMaxima = 10_000;

    public Task<PaginaTurnosAgente> ExecuteAsync(ListarHistoricoAssistenteQuery query, CancellationToken ct = default)
    {
        if (query.Pagina is < 1 or > PaginaMaxima)
            throw new UseCaseValidationException($"A página vai de 1 a {PaginaMaxima}.");
        if (query.Desfecho is { } desfecho && !Enum.IsDefined(desfecho))
            throw new UseCaseValidationException("Resultado do turno inválido.");

        var tamanho = Math.Clamp(query.TamanhoPagina, 1, TamanhoPaginaMaximo);
        return turnos.ListarAsync(query.EmpresaId, new FiltroTurnosAgente(query.ConversaId, query.Desfecho, query.Pagina, tamanho), ct);
    }
}

public sealed record ObterUsoAssistenteQuery(Guid EmpresaId, int? Ano = null, int? Mes = null);

public sealed record UsoAssistenteResult(
    int Ano,
    int Mes,
    int Turnos,
    long TokensEntrada,
    long TokensSaida,
    long TokensCacheLido,
    long TotalTokens,
    decimal CustoEstimado,
    int TurnosSemPreco);

/// <summary>
/// S67 (#2534): consumo do mês só do Assistente, pelo registro dos turnos (não por <c>UsoIa</c>, que mistura o
/// gerador de anúncios). Mês civil de Brasília; sem ano e mês, o mês corrente.
/// </summary>
public sealed class ObterUsoAssistenteUseCase(ITurnoAgenteRepository turnos, TimeProvider relogio)
{
    public async Task<UsoAssistenteResult> ExecuteAsync(ObterUsoAssistenteQuery query, CancellationToken ct = default)
    {
        var hoje = HorarioBrasil.DataOperacional(relogio.GetUtcNow().UtcDateTime);
        var ano = query.Ano ?? hoje.Year;
        var mes = query.Mes ?? hoje.Month;
        if (ano is < 2000 or > 9999 || mes is < 1 or > 12)
            throw new UseCaseValidationException("Mês inválido.");

        var (de, ate) = HorarioBrasil.JanelaMesUtc(new DateOnly(ano, mes, 1));
        var soma = await turnos.SomarAsync(query.EmpresaId, de, ate, ct);
        return new UsoAssistenteResult(ano, mes, soma.Turnos, soma.TokensEntrada, soma.TokensSaida, soma.TokensCacheLido,
            soma.TotalTokens, soma.Custo, soma.TurnosSemPreco);
    }
}
