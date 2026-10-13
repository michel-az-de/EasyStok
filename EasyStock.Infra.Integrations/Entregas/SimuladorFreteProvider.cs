using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Integrations;

namespace EasyStock.Infra.Integrations.Entregas;

/// <summary>Registrar explicitamente como singleton apenas na demonstração isolada. Nunca é fallback de API real.</summary>
public sealed class SimuladorFreteProvider : IFreteProvider, IDisposable
{
    private readonly TimeProvider _relogio;
    private readonly SimuladorEntregaHttpHandler _handler;
    private readonly ConcurrentDictionary<(Guid Empresa, string Cotacao), ContextoCotacao> _cotacoes = new();
    private readonly Dictionary<(Guid Empresa, string Chave), (string Dados, FreteContratacao Contratacao)> _contratacoes = [];
    private readonly SemaphoreSlim _travaContratacao = new(1, 1);

    public SimuladorFreteProvider(string provedor, TimeProvider? relogio = null)
    {
        if (provedor is not ("uber_direct" or "lalamove"))
            throw new ArgumentException("Simulador disponível somente para Uber Direct e Lalamove.", nameof(provedor));
        Provedor = provedor;
        _relogio = relogio ?? TimeProvider.System;
        _handler = new SimuladorEntregaHttpHandler(_relogio);
    }

    public string Provedor { get; }
    public int MaximoDestinos => Provedor == "uber_direct" ? 1 : 15;

    public async Task<FreteCotacao> CotarAsync(FreteSolicitacao solicitacao, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Validar(solicitacao);
        if (solicitacao.Pedidos.Count > MaximoDestinos)
            return new FreteCotacao(Provedor, "", 0, 0, _relogio.GetUtcNow().UtcDateTime, true,
                Provedor == "uber_direct" ? "Uber Direct: cote cada destino separadamente." : "Divida a rota em até 15 destinos por entrega.");

        var agora = _relogio.GetUtcNow();
        var inicio = solicitacao.Pedidos.Max(p => p.JanelaInicioUtc);
        var fim = solicitacao.Pedidos.Min(p => p.JanelaFimUtc);
        if (inicio >= fim)
            return new FreteCotacao(Provedor, "", 0, 0, agora.UtcDateTime, true, "As janelas não coincidem. Separe os pedidos em entregas diferentes.");
        var coleta = new DateTimeOffset(inicio).AddMinutes(-30);
        if (coleta <= agora) coleta = agora.AddMinutes(1);
        var prazo = new DateTimeOffset(fim);
        var duracao = 25 + 5 * solicitacao.Pedidos.Count;
        if (coleta.AddMinutes(duracao) > prazo)
            return new FreteCotacao(Provedor, "", 0, duracao, agora.UtcDateTime, true, "A previsão simulada ultrapassa a janela. Reagende antes de contratar.");

        using var http = CriarHttp();
        FreteCotacao resultado;
        IReadOnlyList<LalamoveParada>? paradas = null;
        if (Provedor == "uber_direct")
        {
            var destino = solicitacao.Pedidos[0].Destino;
            var resposta = await Uber(http, solicitacao.EmpresaId).CotarAsync(new UberCotacaoRequest(
                solicitacao.Origem.Endereco, destino.Endereco, solicitacao.Origem.Latitude, solicitacao.Origem.Longitude,
                destino.Latitude, destino.Longitude, coleta, prazo), ct);
            resultado = new FreteCotacao(Provedor, resposta.Id, resposta.Fee, duracao, resposta.Expires.UtcDateTime, true, null);
        }
        else
        {
            var stops = new[] { solicitacao.Origem }.Concat(solicitacao.Pedidos.Select(p => p.Destino)).Select(Parada).ToArray();
            var resposta = await Lalamove(http).CotarAsync(new LalamoveCotacaoRequest(coleta, "MOTORCYCLE", "pt_BR", stops, false), ct);
            paradas = resposta.Stops;
            var valor = decimal.Parse(resposta.PriceBreakdown.Total, CultureInfo.InvariantCulture);
            resultado = new FreteCotacao(Provedor, resposta.QuotationId, checked((int)(valor * 100)), duracao, resposta.ExpiresAt.UtcDateTime, true, null);
        }
        _cotacoes[(solicitacao.EmpresaId, resultado.Referencia)] = new ContextoCotacao(Assinatura(solicitacao), resultado, coleta, prazo, paradas);
        return resultado;
    }

    public async Task<FreteContratacao> ContratarAsync(FreteSolicitacao solicitacao, string referenciaCotacao, CancellationToken ct)
    {
        Validar(solicitacao);
        ArgumentException.ThrowIfNullOrWhiteSpace(solicitacao.ChaveIdempotencia);
        await _travaContratacao.WaitAsync(ct);
        try
        {
            var assinatura = Assinatura(solicitacao);
            var chave = (solicitacao.EmpresaId, solicitacao.ChaveIdempotencia);
            if (_contratacoes.TryGetValue(chave, out var anterior))
            {
                if (anterior.Dados != assinatura) throw new FreteProviderException("Esta chave já foi usada para outra entrega.");
                return anterior.Contratacao;
            }
            if (!_cotacoes.TryGetValue((solicitacao.EmpresaId, referenciaCotacao), out var cotacao))
                throw new FreteProviderException("Cotação simulada não encontrada ou reiniciada. Faça uma nova cotação.");
            if (cotacao.Dados != assinatura) throw new FreteProviderException("Os pedidos mudaram. Faça uma nova cotação.");
            if (cotacao.Cotacao.ExpiraEmUtc <= _relogio.GetUtcNow().UtcDateTime)
                throw new FreteProviderException("Cotação vencida. Faça uma nova cotação.");
            using var http = CriarHttp();
            string referencia;
            if (Provedor == "uber_direct")
            {
                var destino = solicitacao.Pedidos[0].Destino;
                var resposta = await Uber(http, solicitacao.EmpresaId).CriarAsync(new UberEntregaRequest(referenciaCotacao,
                    solicitacao.Origem.Endereco, solicitacao.Origem.Nome, solicitacao.Origem.Telefone,
                    destino.Endereco, destino.Nome, destino.Telefone,
                    [new UberItem("Pedido " + solicitacao.Pedidos[0].PedidoId.ToString("N"), 1)], solicitacao.ChaveIdempotencia,
                    cotacao.Coleta, cotacao.Prazo), ct);
                referencia = resposta.Id;
            }
            else
            {
                var stops = cotacao.Paradas!;
                var remetente = new LalamoveContato(stops[0].StopId!, solicitacao.Origem.Nome, solicitacao.Origem.Telefone);
                var destinatarios = solicitacao.Pedidos.Select((p, i) => new LalamoveContato(stops[i + 1].StopId!, p.Destino.Nome, p.Destino.Telefone)).ToArray();
                referencia = (await Lalamove(http).CriarAsync(new LalamoveEntregaRequest(referenciaCotacao, remetente, destinatarios), ct)).OrderId;
            }
            var contratacao = new FreteContratacao(referencia, null, "aguardando_entregador", true);
            _contratacoes[chave] = (assinatura, contratacao);
            return contratacao;
        }
        finally { _travaContratacao.Release(); }
    }

    private HttpClient CriarHttp() => new(_handler, disposeHandler: false) { BaseAddress = new Uri("https://entregas-simuladas.invalid/") };
    private static UberDirectHttpClient Uber(HttpClient http, Guid empresa) => new(http, "sim_" + empresa.ToString("N"));
    private LalamoveHttpClient Lalamove(HttpClient http) => new(http, "simulado-sem-credencial", "simulado-sem-segredo", _relogio);
    private static LalamoveParada Parada(FreteEndereco endereco) => new(new LalamoveCoordenadas(
        endereco.Latitude.ToString("R", CultureInfo.InvariantCulture), endereco.Longitude.ToString("R", CultureInfo.InvariantCulture)), endereco.Endereco);
    private static string Assinatura(FreteSolicitacao solicitacao) => JsonSerializer.Serialize(new { solicitacao.EmpresaId, solicitacao.Origem, solicitacao.Pedidos });

    private static void Validar(FreteSolicitacao solicitacao)
    {
        if (solicitacao.EmpresaId == Guid.Empty) throw new ArgumentException("Empresa obrigatória.");
        if (solicitacao.Pedidos.Count == 0 || solicitacao.Pedidos.Any(p => p.PedidoId == Guid.Empty)
            || solicitacao.Pedidos.Select(p => p.PedidoId).Distinct().Count() != solicitacao.Pedidos.Count)
            throw new ArgumentException("Selecione pedidos distintos para a entrega.");
        foreach (var endereco in new[] { solicitacao.Origem }.Concat(solicitacao.Pedidos.Select(p => p.Destino)))
        {
            UberDirectHttpClient.ValidarEndereco(endereco.Endereco, endereco.Latitude, endereco.Longitude);
            ArgumentException.ThrowIfNullOrWhiteSpace(endereco.Nome);
            ArgumentException.ThrowIfNullOrWhiteSpace(endereco.Telefone);
        }
        if (solicitacao.Pedidos.Any(p => p.JanelaInicioUtc.Kind != DateTimeKind.Utc || p.JanelaFimUtc.Kind != DateTimeKind.Utc || p.JanelaFimUtc <= p.JanelaInicioUtc))
            throw new ArgumentException("Informe uma janela válida em UTC.");
    }

    public void Dispose() { _handler.Dispose(); _travaContratacao.Dispose(); }
    private sealed record ContextoCotacao(string Dados, FreteCotacao Cotacao, DateTimeOffset Coleta, DateTimeOffset Prazo, IReadOnlyList<LalamoveParada>? Paradas);
}
