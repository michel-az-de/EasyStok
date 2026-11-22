using System.Collections.Concurrent;

namespace EasyStock.Application.Services.Atendimento;

/// <summary>
/// #3117: mensagens que o <see cref="VigiaPonteWppConnect"/> já leu na ponte e que nunca vão contar (da própria loja, de
/// tipo que o webhook não recebe, anteriores à janela). Sem isto, conversas assim no topo da lista gastam o teto de
/// consultas em toda rodada e escondem a conversa realmente muda. Vive só em memória, uma por processo; ao encher, esvazia.
/// </summary>
public sealed class MemoriaVigiaPonteWppConnect
{
    private const int Teto = 5_000;
    private readonly ConcurrentDictionary<string, byte> _descartadas = new(StringComparer.Ordinal);

    public bool JaDescartada(string idExterno) => _descartadas.ContainsKey(idExterno);

    public void Descartar(string idExterno)
    {
        if (_descartadas.Count >= Teto) _descartadas.Clear();
        _descartadas[idExterno] = 0;
    }
}
