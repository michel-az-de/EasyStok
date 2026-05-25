namespace EasyStock.Application.UseCases.Inteligencia.Sazonalidade;

public sealed record SazonalidadeInteligenciaResult(
    int Ano,
    int Mes,
    int TotalSaidas,
    decimal ValorTotal);
