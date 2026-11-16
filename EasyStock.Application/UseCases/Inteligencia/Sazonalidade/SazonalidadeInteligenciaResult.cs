namespace EasyStock.Application.UseCases.Inteligencia.Sazonalidade;

public sealed record SazonalidadeInteligenciaResult(
    int Ano,
    int Mes,
    decimal TotalSaidas,
    decimal ValorTotal);
