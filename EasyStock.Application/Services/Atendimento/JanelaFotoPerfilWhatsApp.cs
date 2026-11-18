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
        var chave = (empresaId, clienteId);
        var agora = relogio.GetUtcNow();
        while (true)
        {
            if (!_ultimaConsulta.TryGetValue(chave, out var ultima))
            {
                if (_ultimaConsulta.TryAdd(chave, agora)) return true;
                continue;
            }
            if (agora - ultima < Intervalo) return false;
            if (_ultimaConsulta.TryUpdate(chave, agora, ultima)) return true;
        }
    }

    /// <summary>Devolve a reserva quando a consulta falhou e não disse nada sobre a foto.</summary>
    public void Liberar(Guid empresaId, Guid clienteId) => _ultimaConsulta.TryRemove((empresaId, clienteId), out _);
}
