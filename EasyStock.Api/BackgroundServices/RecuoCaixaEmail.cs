namespace EasyStock.Api.BackgroundServices;

/// <summary>
/// Recuo e quarentena da leitura das caixas de e-mail (#1642, item 3), em memória do processo. Caixa que não abre
/// (senha trocada, servidor fora) espera o dobro a cada falha seguida, até <see cref="Teto"/>; a primeira leitura
/// que dá certo zera. E-mail que falhou ao gravar fica de fora do lote por <see cref="DuracaoQuarentena"/>, para não
/// voltar na frente da fila toda rodada; continua não lido na caixa. Reiniciar o processo zera tudo: é o mesmo que
/// tentar de novo, sem perder e-mail.
/// </summary>
public sealed class RecuoCaixaEmail(TimeSpan intervalo)
{
    public static readonly TimeSpan Teto = TimeSpan.FromHours(1);
    public static readonly TimeSpan DuracaoQuarentena = TimeSpan.FromMinutes(30);

    private readonly Dictionary<Guid, Estado> _estados = [];

    public bool PodeLer(Guid empresaId, DateTime agora) =>
        !_estados.TryGetValue(empresaId, out var estado) || agora >= estado.LiberadaEm;

    /// <summary>Ids da caixa que o lote pula; o use case acrescenta o que falhar. Esvazia a cada <see cref="DuracaoQuarentena"/>.</summary>
    public ISet<string> Quarentena(Guid empresaId, DateTime agora)
    {
        var estado = Obter(empresaId);
        if (estado.QuarentenaDesde is not { } desde || agora - desde >= DuracaoQuarentena)
        {
            estado.Quarentena.Clear();
            estado.QuarentenaDesde = agora;
        }
        return estado.Quarentena;
    }

    /// <summary>Registra a caixa que não abriu e devolve quanto ela espera até a próxima tentativa.</summary>
    public TimeSpan RegistrarFalha(Guid empresaId, DateTime agora)
    {
        var estado = Obter(empresaId);
        estado.FalhasSeguidas++;
        var segundos = intervalo.TotalSeconds * Math.Pow(2, Math.Min(estado.FalhasSeguidas, 16));
        var espera = segundos >= Teto.TotalSeconds ? Teto : TimeSpan.FromSeconds(segundos);
        estado.LiberadaEm = agora + espera;
        return espera;
    }

    public void RegistrarSucesso(Guid empresaId)
    {
        if (!_estados.TryGetValue(empresaId, out var estado)) return;
        estado.FalhasSeguidas = 0;
        estado.LiberadaEm = DateTime.MinValue;
    }

    private Estado Obter(Guid empresaId)
    {
        if (!_estados.TryGetValue(empresaId, out var estado))
            _estados[empresaId] = estado = new Estado();
        return estado;
    }

    private sealed class Estado
    {
        public int FalhasSeguidas;
        public DateTime LiberadaEm = DateTime.MinValue;
        public DateTime? QuarentenaDesde;
        public readonly HashSet<string> Quarentena = [];
    }
}
