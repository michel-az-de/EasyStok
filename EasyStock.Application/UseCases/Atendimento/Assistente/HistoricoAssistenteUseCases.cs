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
    int TurnosSemPreco,
    long? CotaTokensMes = null,
    int? Percentual = null,
    bool Alerta = false,
    int RespostasSemIa = 0,
    long? TokensPoupadosEstimados = null);

/// <summary>
/// S67 (#2534): consumo do mês só do Assistente, pelo registro dos turnos (não por <c>UsoIa</c>, que mistura o
/// gerador de anúncios). Mês civil de Brasília; sem ano e mês, o mês corrente. #3370: os tokens têm o piso das chamadas
/// ligadas aos turnos em <c>chamada_ia</c> (<see cref="ITurnoAgenteRepository.SomarAsync"/>), e o custo e as contagens
/// seguem dos turnos: com o piso ativo o cartão mostra mais tokens do que o custo cobre.
/// </summary>
public sealed class ObterUsoAssistenteUseCase(ITurnoAgenteRepository turnos, TimeProvider relogio,
    IConfiguracaoAtendimentoRepository? configuracoes = null)
{
    public const int PercentualAlerta = 80;

    public async Task<UsoAssistenteResult> ExecuteAsync(ObterUsoAssistenteQuery query, CancellationToken ct = default)
    {
        var hoje = HorarioBrasil.DataOperacional(relogio.GetUtcNow().UtcDateTime);
        var ano = query.Ano ?? hoje.Year;
        var mes = query.Mes ?? hoje.Month;
        if (ano is < 2000 or > 9999 || mes is < 1 or > 12)
            throw new UseCaseValidationException("Mês inválido.");

        var (de, ate) = HorarioBrasil.JanelaMesUtc(new DateOnly(ano, mes, 1));
        var soma = await turnos.SomarAsync(query.EmpresaId, de, ate, ct);
        var resultado = new UsoAssistenteResult(ano, mes, soma.Turnos, soma.TokensEntrada, soma.TokensSaida, soma.TokensCacheLido,
            soma.TotalTokens, soma.Custo, soma.TurnosSemPreco)
        {
            RespostasSemIa = soma.RespostasSemIa,
            TokensPoupadosEstimados = soma.TokensPoupadosEstimados,
        };

        // S69 (#2853): a mesma soma que a etapa da cota usa no turno; alerta a partir de 80%.
        var cota = configuracoes is null ? null : (await configuracoes.GetByEmpresaIdAsync(query.EmpresaId))?.CotaTokensMes;
        if (cota is not > 0) return resultado;
        var percentual = (int)Math.Min(int.MaxValue, soma.TotalTokens * 100 / cota.Value);
        return resultado with { CotaTokensMes = cota, Percentual = percentual, Alerta = percentual >= PercentualAlerta };
    }
}
