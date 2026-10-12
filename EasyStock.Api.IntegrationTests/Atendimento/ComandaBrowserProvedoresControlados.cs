using System.Collections.Concurrent;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Ports.Output.Pagamentos;
using NSubstitute;

namespace EasyStock.Api.IntegrationTests.Atendimento;

public sealed partial class PedidoConversaConcorrenciaTests
{
    // Apenas fronteiras externas controladas. Serviço de agente, ferramentas, ledger e dispatcher são reais.
    private sealed class EstornosBrowserControlados
    {
        private readonly ConcurrentDictionary<string, EstornoMercadoPagoResult> _porChave = new();
        private int _id = 900000;
        private string _modo = "approved";
        private readonly ConcurrentDictionary<string, string> _modos = new();
        public EstornosBrowserControlados(IMercadoPagoClient mp)
        {
            mp.ListarEstornosAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(c =>
                (IReadOnlyList<EstornoMercadoPagoResult>)_porChave.Values.Where(e => e.PagamentoId == c.Arg<string>()).ToArray());
            mp.EstornarAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(c =>
                _porChave.GetOrAdd(c.ArgAt<string>(2), _ => new EstornoMercadoPagoResult(Interlocked.Increment(ref _id).ToString(),
                    c.Arg<decimal>(), _modos.GetValueOrDefault(c.ArgAt<string>(0), _modo), c.ArgAt<string>(0), DateTime.UtcNow)));
            mp.ConsultarEstornoAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(c =>
                _porChave.Values.SingleOrDefault(e => e.PagamentoId == c.ArgAt<string>(0) && e.EstornoId == c.ArgAt<string>(1)));
        }
        public void DefinirModo(string situacao, IEnumerable<string>? pagamentos = null)
        {
            if (situacao is not ("pending" or "approved" or "rejected")) throw new ArgumentException("Situação controlada inválida.");
            if (pagamentos is null) _modo = situacao;
            else foreach (var id in pagamentos) _modos[id] = situacao;
        }
        public void Conciliar(string pagamentoId, string situacao)
        {
            DefinirModo(situacao, [pagamentoId]);
            foreach (var entrada in _porChave.Where(p => p.Value.PagamentoId == pagamentoId))
                _porChave[entrada.Key] = entrada.Value with { Status = situacao };
        }
    }

    private sealed class LlmBrowserControlado : IAgenteLlmClient
    {
        public bool Disponivel => true;
        public ConcurrentQueue<string> Resultados { get; } = new();
        private readonly AsyncLocal<ConcurrentQueue<string>?> _turno = new();
        private readonly AsyncLocal<string?> _resposta = new();
        private readonly AsyncLocal<bool> _consultarAntesDaResposta = new();
        public ConcurrentQueue<string> IniciarTurno(string? respostaControlada = null, bool consultarAntesDaResposta = false)
        {
            var resultados = new ConcurrentQueue<string>();
            _turno.Value = resultados; _resposta.Value = respostaControlada;
            _consultarAntesDaResposta.Value = consultarAntesDaResposta;
            return resultados;
        }
        public Task<RespostaLlm> EnviarAsync(RequisicaoLlm requisicao, CancellationToken ct = default)
        {
            if (_resposta.Value is { } resposta && !_consultarAntesDaResposta.Value)
                return Task.FromResult(new RespostaLlm("end_turn", [new BlocoTextoLlm(resposta)], 1, 1));
            var resultado = requisicao.Mensagens.Last().Conteudo.OfType<BlocoResultadoFerramentaLlm>().FirstOrDefault();
            if (resultado is not null)
            {
                Resultados.Enqueue(resultado.Conteudo);
                _turno.Value?.Enqueue(resultado.Conteudo);
                return Task.FromResult(new RespostaLlm("end_turn", [new BlocoTextoLlm(_resposta.Value ?? "Confira os dados atuais da sua comanda: " + resultado.Conteudo)], 1, 1));
            }
            var texto = string.Join(" ", requisicao.Mensagens.Last().Conteudo.OfType<BlocoTextoLlm>().Select(b => b.Texto));
            var ferramenta = !_consultarAntesDaResposta.Value && (texto.Contains("adicionar", StringComparison.OrdinalIgnoreCase) || texto.Contains("cardápio", StringComparison.OrdinalIgnoreCase)
                || texto.Contains("montar", StringComparison.OrdinalIgnoreCase)) ? "enviar_cardapio_imagem" : "consultar_pedido";
            return Task.FromResult(new RespostaLlm(RespostaLlm.StopToolUse,
                [new BlocoUsoFerramentaLlm("toolu.controlado", ferramenta, JsonSerializer.SerializeToElement(new { }))], 1, 1));
        }
    }
}
