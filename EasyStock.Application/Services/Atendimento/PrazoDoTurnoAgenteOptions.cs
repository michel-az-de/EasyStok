using Microsoft.Extensions.Configuration;

namespace EasyStock.Application.Services.Atendimento;

/// <summary>
/// #3291 (R3 da revisão #3271): prazo total das chamadas ao modelo em um turno do Assistente. Sem ele, o pior caso era de
/// 6 voltas de até 60 s, cada uma com nova tentativa. O prazo só corta a chamada ao modelo: ferramenta já iniciada e
/// envio ao cliente terminam. Configuração em <c>Atendimento:FilaTurnoAgente:PrazoTurnoSegundos</c>; zero ou negativo
/// desliga o prazo.
/// </summary>
public sealed class PrazoDoTurnoAgenteOptions
{
    public const string Chave = "Atendimento:FilaTurnoAgente:PrazoTurnoSegundos";
    public const int PadraoSegundos = 180;

    // CancellationTokenSource.CancelAfter recusa mais que ~49 dias; um dia já é muito acima de qualquer turno.
    private const int MaximoSegundos = 86_400;

    public int Segundos { get; set; } = PadraoSegundos;

    /// <summary>Prazo do turno; nulo quando desligado.</summary>
    public TimeSpan? Prazo => Segundos > 0 ? TimeSpan.FromSeconds(Math.Min(Segundos, MaximoSegundos)) : null;

    public void Ler(IConfiguration configuracao)
    {
        if (int.TryParse(configuracao[Chave], out var segundos)) Segundos = segundos;
    }
}
