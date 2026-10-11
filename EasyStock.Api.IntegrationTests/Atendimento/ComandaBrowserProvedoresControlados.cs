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
        public EstornosBrowserControlados(IMercadoPagoClient mp)
        {
            mp.ListarEstornosAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(c =>
                (IReadOnlyList<EstornoMercadoPagoResult>)_porChave.Values.Where(e => e.PagamentoId == c.Arg<string>()).ToArray());
            mp.EstornarAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(c =>
                _porChave.GetOrAdd(c.ArgAt<string>(2), _ => new EstornoMercadoPagoResult(Interlocked.Increment(ref _id).ToString(),
                    c.Arg<decimal>(), _modo, c.ArgAt<string>(0), DateTime.UtcNow)));
            mp.ConsultarEstornoAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(c =>
                _porChave.Values.SingleOrDefault(e => e.PagamentoId == c.ArgAt<string>(0) && e.EstornoId == c.ArgAt<string>(1)));
        }
        public void DefinirModo(string situacao)
        {
            if (situacao is not ("pending" or "approved" or "rejected")) throw new ArgumentException("Situação controlada inválida.");
            _modo = situacao;
        }
        public void Conciliar(string pagamentoId, string situacao)
        {
            DefinirModo(situacao);
            foreach (var entrada in _porChave.Where(p => p.Value.PagamentoId == pagamentoId))
                _porChave[entrada.Key] = entrada.Value with { Status = situacao };
        }
    }

    private sealed class LlmBrowserControlado : IAgenteLlmClient
    {
        public bool Disponivel => true;
        public ConcurrentQueue<string> Resultados { get; } = new();
        public Task<RespostaLlm> EnviarAsync(RequisicaoLlm requisicao, CancellationToken ct = default)
        {
            var resultado = requisicao.Mensagens.Last().Conteudo.OfType<BlocoResultadoFerramentaLlm>().FirstOrDefault();
            if (resultado is not null)
            {
                Resultados.Enqueue(resultado.Conteudo);
                return Task.FromResult(new RespostaLlm("end_turn", [new BlocoTextoLlm("Confira os dados atuais da sua comanda: " + resultado.Conteudo)], 1, 1));
            }
            var texto = string.Join(" ", requisicao.Mensagens.Last().Conteudo.OfType<BlocoTextoLlm>().Select(b => b.Texto));
            var ferramenta = texto.Contains("adicionar", StringComparison.OrdinalIgnoreCase) || texto.Contains("cardápio", StringComparison.OrdinalIgnoreCase)
                || texto.Contains("montar", StringComparison.OrdinalIgnoreCase) ? "enviar_cardapio_imagem" : "consultar_pedido";
            return Task.FromResult(new RespostaLlm(RespostaLlm.StopToolUse,
                [new BlocoUsoFerramentaLlm("toolu.controlado", ferramenta, JsonSerializer.SerializeToElement(new { }))], 1, 1));
        }
    }
}
