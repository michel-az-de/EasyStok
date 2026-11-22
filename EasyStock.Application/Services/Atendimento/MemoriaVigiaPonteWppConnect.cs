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

    private readonly ConcurrentDictionary<string, (DateTimeOffset Desde, bool AposQueda)> _conectada = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, (DateTimeOffset Desde, int Observacoes)> _desconectada = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _anterioresAoProcesso = new(StringComparer.Ordinal);

    /// <summary>
    /// #3138: desde quando este processo vê a sessão conectada sem interrupção (a observação, não a conexão real), e se
    /// antes disso ele a viu desconectada ou sem resposta. Uma sessão por loja: estes dicionários não têm teto.
    /// </summary>
    public (DateTimeOffset Desde, bool AposQueda) ObservarConectada(string sessao, DateTimeOffset agora)
    {
        var caiu = _desconectada.TryRemove(sessao, out _);
        return caiu ? _conectada[sessao] = (agora, true) : _conectada.GetOrAdd(sessao, (agora, false));
    }

    /// <summary>#3138: desde quando, e por quantas observações seguidas, este processo vê a sessão desconectada ou sem resposta.</summary>
    public (DateTimeOffset Desde, int Observacoes) ObservarDesconectada(string sessao, DateTimeOffset agora)
    {
        _conectada.TryRemove(sessao, out _);
        return _desconectada.AddOrUpdate(sessao, (agora, 1), (_, a) => (a.Desde, a.Observacoes + 1));
    }

    /// <summary>Mensagem sem entrega e anterior à primeira observação deste processo: lembrada com a data, para não gastar consulta de novo.</summary>
    public bool AnteriorAoProcesso(string idExterno, out DateTimeOffset quando) => _anterioresAoProcesso.TryGetValue(idExterno, out quando);

    public void LembrarAnteriorAoProcesso(string idExterno, DateTimeOffset quando)
    {
        if (_anterioresAoProcesso.Count >= Teto) _anterioresAoProcesso.Clear();
        _anterioresAoProcesso[idExterno] = quando;
    }

    public bool JaDescartada(string idExterno) => _descartadas.ContainsKey(idExterno);

    public void Descartar(string idExterno)
    {
        if (_descartadas.Count >= Teto) _descartadas.Clear();
        _descartadas[idExterno] = 0;
    }
}
