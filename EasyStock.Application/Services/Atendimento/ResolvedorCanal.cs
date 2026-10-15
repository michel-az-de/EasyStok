using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.Services.Atendimento;

/// <summary>
/// Escolhe o adaptador de <see cref="ICanalMensageria"/> pelo canal da conversa (S34, ADR-0051).
/// Canal sem adaptador registrado recusa o envio: é o <c>CANAL_DESCONHECIDO</c> do protótipo.
/// </summary>
public sealed class ResolvedorCanal(IEnumerable<ICanalMensageria> adaptadores)
{
    private readonly IReadOnlyDictionary<CanalConversa, ICanalMensageria> _porCanal =
        adaptadores.ToDictionary(a => a.Canal);

    public ICanalMensageria Obter(CanalConversa canal) =>
        _porCanal.TryGetValue(canal, out var adaptador) ? adaptador : throw new CanalNaoSuportadoException(canal);
}

public sealed class CanalNaoSuportadoException(CanalConversa canal)
    : Exception($"Canal {canal} sem adaptador de envio registrado.")
{
    public CanalConversa Canal { get; } = canal;
}
