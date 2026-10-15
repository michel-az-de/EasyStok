using System.Net;
using System.Text;

namespace EasyStock.TestHelpers.Http;

/// <summary>
/// <see cref="HttpMessageHandler"/> roteável para testar adapter de terceiros sem rede (#1634).
/// Substitui os handlers redeclarados em cada arquivo de teste (<c>HandlerFalso</c>, <c>StubHandler</c>).
///
/// <para>
/// Uso típico:
/// <code>
/// var falso = new HttpFalso();
/// falso.Quando(HttpMethod.Post, "/v1/pagamentos")
///     .Responde(HttpStatusCode.InternalServerError)
///     .Responde(HttpStatusCode.Created, """{"id":"1"}""");
/// var adapter = new MeuAdapter(falso.CriarClient("https://provedor.test/"));
/// ...
/// falso.UltimaChamada!.Cabecalho("X-Idempotency-Key").Should().Be("chave-1");
/// </code>
/// </para>
///
/// <para>
/// Regras: a primeira rota registrada que casa responde; cada rota guarda uma sequência de respostas e
/// repete a última quando a sequência acaba; toda chamada é gravada, inclusive a não prevista, que lança
/// <see cref="InvalidOperationException"/> dizendo método, URL e as rotas registradas.
/// </para>
/// </summary>
public sealed class HttpFalso : HttpMessageHandler
{
    private readonly object _trava = new();
    private readonly List<RotaHttpFalsa> _rotas = [];
    private readonly List<ChamadaHttp> _chamadas = [];

    /// <summary>Todas as chamadas recebidas, na ordem.</summary>
    public IReadOnlyList<ChamadaHttp> Chamadas
    {
        get { lock (_trava) return _chamadas.ToArray(); }
    }

    /// <summary>A chamada mais recente, ou <see langword="null"/> se nada chegou ao handler.</summary>
    public ChamadaHttp? UltimaChamada
    {
        get { lock (_trava) return _chamadas.Count == 0 ? null : _chamadas[^1]; }
    }

    /// <summary>
    /// Registra rota por método + caminho absoluto, sem query (<c>/v1/pagamentos/1</c>; a barra inicial é opcional).
    /// Para casar pela query, use a sobrecarga com predicado.
    /// </summary>
    public RotaHttpFalsa Quando(HttpMethod metodo, string caminho)
    {
        ArgumentNullException.ThrowIfNull(metodo);
        ArgumentNullException.ThrowIfNull(caminho);
        if (caminho.Contains('?'))
            throw new ArgumentException(
                $"O caminho '{caminho}' tem query. A rota casa só pelo caminho; para query use Quando(predicado).",
                nameof(caminho));

        var absoluto = caminho.StartsWith('/') ? caminho : "/" + caminho;
        return Registrar(
            r => r.Method == metodo && string.Equals(r.RequestUri?.AbsolutePath, absoluto, StringComparison.Ordinal),
            $"{metodo.Method} {absoluto}");
    }

    /// <summary>Registra rota por predicado sobre a requisição. A <paramref name="descricao"/> aparece no erro de rota não prevista.</summary>
    public RotaHttpFalsa Quando(Func<HttpRequestMessage, bool> predicado, string descricao = "predicado")
    {
        ArgumentNullException.ThrowIfNull(predicado);
        return Registrar(predicado, descricao);
    }

    /// <summary>Rota coringa: casa qualquer requisição. Registre por último, porque a primeira rota que casa responde.</summary>
    public RotaHttpFalsa QuandoQualquer() => Registrar(_ => true, "qualquer requisição");

    /// <summary>Devolve um <see cref="HttpClient"/> sobre este handler, com <c>BaseAddress</c>. Descartar o client não descarta o handler.</summary>
    public HttpClient CriarClient(string baseAddress = "https://provedor.test/") =>
        new(this, disposeHandler: false) { BaseAddress = new Uri(baseAddress) };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Lê o corpo agora: depois do envio o chamador descarta o conteúdo e a leitura falha.
        var corpo = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var chamada = new ChamadaHttp(request.Method, request.RequestUri!, CopiarCabecalhos(request), corpo);

        RespostaHttpFalsa? resposta;
        string rotas;
        lock (_trava)
        {
            _chamadas.Add(chamada);
            resposta = _rotas.FirstOrDefault(r => r.Casa(request))?.Proxima();
            rotas = _rotas.Count == 0
                ? "nenhuma rota registrada"
                : string.Join("; ", _rotas.Select(r => r.Descricao));
        }

        if (resposta is null)
            throw new InvalidOperationException(
                $"HttpFalso: chamada não prevista {request.Method.Method} {request.RequestUri?.AbsoluteUri}. Rotas registradas: {rotas}.");

        if (resposta.Atraso is { } atraso)
            await Task.Delay(atraso, cancellationToken);
        if (resposta.Excecao is not null)
            throw resposta.Excecao;

        var mensagem = new HttpResponseMessage(resposta.Status)
        {
            RequestMessage = request,
            Content = new StringContent(resposta.Corpo ?? string.Empty, Encoding.UTF8, "application/json"),
        };
        foreach (var (nome, valor) in resposta.Cabecalhos)
        {
            if (!mensagem.Headers.TryAddWithoutValidation(nome, valor))
                mensagem.Content.Headers.TryAddWithoutValidation(nome, valor);
        }
        return mensagem;
    }

    private RotaHttpFalsa Registrar(Func<HttpRequestMessage, bool> predicado, string descricao)
    {
        var rota = new RotaHttpFalsa(predicado, descricao, _trava);
        lock (_trava) _rotas.Add(rota);
        return rota;
    }

    private static Dictionary<string, IReadOnlyList<string>> CopiarCabecalhos(HttpRequestMessage request)
    {
        var copia = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (nome, valores) in request.Headers)
            copia[nome] = valores.ToArray();
        if (request.Content is not null)
        {
            foreach (var (nome, valores) in request.Content.Headers)
                copia[nome] = valores.ToArray();
        }
        return copia;
    }
}

/// <summary>
/// Rota do <see cref="HttpFalso"/>. Cada <c>Responde</c>, <c>Lanca</c> ou <c>Demora</c> acrescenta um passo à
/// sequência; a 1ª chamada recebe o 1º passo, e o último passo se repete nas chamadas seguintes.
/// </summary>
public sealed class RotaHttpFalsa
{
    private readonly Func<HttpRequestMessage, bool> _predicado;
    private readonly object _trava;
    private readonly List<RespostaHttpFalsa> _sequencia = [];
    private int _proxima;

    internal RotaHttpFalsa(Func<HttpRequestMessage, bool> predicado, string descricao, object trava)
    {
        _predicado = predicado;
        _trava = trava;
        Descricao = descricao;
    }

    internal string Descricao { get; }

    /// <summary>Responde com status, corpo (JSON como texto) e cabeçalhos opcionais.</summary>
    public RotaHttpFalsa Responde(
        HttpStatusCode status, string? corpo = null, IReadOnlyDictionary<string, string>? cabecalhos = null) =>
        Acrescentar(new RespostaHttpFalsa(status, corpo, cabecalhos, Excecao: null, Atraso: null));

    /// <summary>Lança a exceção: <see cref="HttpRequestException"/> para falha de rede, <see cref="TaskCanceledException"/> para timeout.</summary>
    public RotaHttpFalsa Lanca(Exception excecao)
    {
        ArgumentNullException.ThrowIfNull(excecao);
        return Acrescentar(new RespostaHttpFalsa(default, null, null, excecao, Atraso: null));
    }

    /// <summary>Espera <paramref name="atraso"/> antes de responder. O atraso respeita o <see cref="CancellationToken"/> da chamada.</summary>
    public RotaHttpFalsa Demora(
        TimeSpan atraso, HttpStatusCode status = HttpStatusCode.OK, string? corpo = null,
        IReadOnlyDictionary<string, string>? cabecalhos = null) =>
        Acrescentar(new RespostaHttpFalsa(status, corpo, cabecalhos, Excecao: null, atraso));

    internal bool Casa(HttpRequestMessage request) => _predicado(request);

    // Chamado pelo HttpFalso já dentro da trava.
    internal RespostaHttpFalsa Proxima()
    {
        if (_sequencia.Count == 0)
            throw new InvalidOperationException(
                $"HttpFalso: a rota '{Descricao}' foi registrada sem resposta. Encadeie Responde, Lanca ou Demora.");

        var resposta = _sequencia[Math.Min(_proxima, _sequencia.Count - 1)];
        _proxima++;
        return resposta;
    }

    private RotaHttpFalsa Acrescentar(RespostaHttpFalsa resposta)
    {
        lock (_trava) _sequencia.Add(resposta);
        return this;
    }
}

/// <summary>Chamada gravada pelo <see cref="HttpFalso"/>. O corpo é <see langword="null"/> quando a requisição veio sem conteúdo.</summary>
public sealed record ChamadaHttp(
    HttpMethod Metodo,
    Uri Uri,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Cabecalhos,
    string? Corpo)
{
    /// <summary>Valor do cabeçalho (nome sem diferenciar maiúsculas), ou <see langword="null"/> se não veio. Vários valores saem separados por vírgula.</summary>
    public string? Cabecalho(string nome) =>
        Cabecalhos.TryGetValue(nome, out var valores) ? string.Join(", ", valores) : null;
}

internal sealed record RespostaHttpFalsa(
    HttpStatusCode Status,
    string? Corpo,
    IReadOnlyDictionary<string, string>? CabecalhosOuNulo,
    Exception? Excecao,
    TimeSpan? Atraso)
{
    public IReadOnlyDictionary<string, string> Cabecalhos { get; } =
        CabecalhosOuNulo ?? new Dictionary<string, string>();
}
