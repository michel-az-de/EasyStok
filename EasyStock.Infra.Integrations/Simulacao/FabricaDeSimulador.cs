using EasyStock.Application.Ports.Output;

namespace EasyStock.Infra.Integrations.Simulacao;

/// <summary>
/// Único caminho para construir um <see cref="ISimulador"/> (I3.1, #1667; invariante 1): em Production
/// lança, sem exceção. O registro do adapter já recusa a flag na subida; isto é a segunda trava, para o
/// caso de alguém registrar um simulador por outro caminho.
/// </summary>
public static class FabricaDeSimulador
{
    /// <param name="ambiente">Nome do ambiente do host (<c>IHostEnvironment.EnvironmentName</c>).</param>
    public static T Criar<T>(string? ambiente, Func<T> criar) where T : ISimulador
    {
        if (EmProducao(ambiente))
            throw new InvalidOperationException(
                $"{typeof(T).Name} é um simulador e não pode ser construído em Production.");
        return criar();
    }

    /// <summary>Sem ambiente conhecido vale Production (falha fechado).</summary>
    public static bool EmProducao(string? ambiente) =>
        string.IsNullOrWhiteSpace(ambiente) || string.Equals(ambiente.Trim(), "Production", StringComparison.OrdinalIgnoreCase);
}