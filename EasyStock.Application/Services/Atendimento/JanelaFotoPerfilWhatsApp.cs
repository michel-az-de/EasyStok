using System.Collections.Concurrent;

namespace EasyStock.Application.Services.Atendimento;

/// <summary>
/// #2838: toda mensagem do WhatsApp enfileira a busca da foto do perfil; esta janela deixa a ponte ser consultada no
/// máximo uma vez por cliente a cada <see cref="Intervalo"/>. Em memória, como a fila: um restart só antecipa a
/// próxima consulta.
/// </summary>
public sealed class JanelaFotoPerfilWhatsApp(TimeProvider relogio)
{
    public static readonly TimeSpan Intervalo = TimeSpan.FromHours(12);

    private readonly ConcurrentDictionary<(Guid EmpresaId, Guid ClienteId), DateTimeOffset> _ultimaConsulta = new();

    /// <summary>Reserva a consulta do cliente; <c>false</c> quando outra já saiu dentro da janela.</summary>
    public bool Reservar(Guid empresaId, Guid clienteId)
    {
        var agora = relogio.GetUtcNow();
        var reservou = false;
        _ultimaConsulta.AddOrUpdate((empresaId, clienteId),
            _ => { reservou = true; return agora; },
            (_, ultima) =>
            {
                if (agora - ultima < Intervalo) return ultima;
                reservou = true;
                return agora;
            });
        return reservou;
    }
}
