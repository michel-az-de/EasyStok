using System.Text.RegularExpressions;
using FluentAssertions;

namespace EasyStock.ArchitectureTests;

/// <summary>
/// Guard (#1230, pendência da S21): todo caminho da Application que muda o status de um pedido ou cria um
/// pedido precisa gravar o início previsto (<c>CalculadoraInicioPrevistoPedido.AplicarNaFilaAsync</c> ou
/// <c>Pedido.DefinirInicioPrevisto</c>). Sem isso o pedido entra na fila sem início previsto, o KDS cai na
/// regra provisória e o <c>PedidoAtrasoJob</c> nunca avisa o atraso.
///
/// <para>
/// O cálculo não pode morar em <c>Pedido.MudarStatus</c>: a calculadora lê vaga, itens e configuração por
/// porta de I/O. Este teste é o que impede um caminho novo de esquecer. Arquivo que muda status sem nunca
/// levar o pedido a <c>Aguardando</c> entra em <see cref="SemEntradaNaFila"/> com o motivo.
/// </para>
/// </summary>
[Trait("Category", "Architecture")]
public class InicioPrevistoNaFilaTests
{
    private static readonly Regex MudaStatusOuCriaPedido = new(
        @"\.MudarStatus\(|\b(Pedido|PedidoEntity|DomainPedido)\.Criar\(|\bStatus\s*=\s*StatusPedidoMapper\.Aguardando\b",
        RegexOptions.Compiled);

    private static readonly Regex GravaInicioPrevisto = new(
        @"\.AplicarNaFilaAsync\(|\.DefinirInicioPrevisto\(",
        RegexOptions.Compiled);

    /// <summary>Arquivos que mudam status ou criam pedido sem pôr o pedido em <c>Aguardando</c>. Caminho relativo à raiz.</summary>
    private static readonly Dictionary<string, string> SemEntradaNaFila = new(StringComparer.OrdinalIgnoreCase)
    {
        ["EasyStock.Application/UseCases/Atendimento/Entregas/ViagensUseCases.cs"] =
            "só Pronto -> SaiuParaEntrega -> Entregue",
        ["EasyStock.Application/Services/Storefront/CheckoutCoreService.cs"] =
            "nasce Rascunho/AguardandoPagamento; entra na fila pelo ConfirmarPagamentoPedidoUseCase",
        ["EasyStock.Application/UseCases/Storefront/Checkout/IniciarCheckoutGuestUseCase.cs"] =
            "nasce AguardandoAprovacaoBaba; atraso só vale em Aguardando",
    };

    [Fact]
    public void CaminhoQueMudaStatusDePedido_GravaInicioPrevisto()
    {
        var root = RepoPaths.FindRepoRoot();
        var offenders = new List<string>();

        foreach (var file in Directory.GetFiles(Path.Combine(root, "EasyStock.Application"), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)) continue;
            if (file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)) continue;
            var rel = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
            if (SemEntradaNaFila.ContainsKey(rel)) continue;

            var texto = File.ReadAllText(file);
            if (MudaStatusOuCriaPedido.IsMatch(texto) && !GravaInicioPrevisto.IsMatch(texto))
                offenders.Add(rel);
        }

        offenders.Should().BeEmpty(
            "pedido que entra na fila precisa de início previsto (S21, #1230): chame " +
            "CalculadoraInicioPrevistoPedido.AplicarNaFilaAsync depois de mudar o status (e com o pedido gravado), " +
            "ou, se o caminho nunca leva o pedido a Aguardando, registre o arquivo em SemEntradaNaFila com o motivo.");
    }

    [Fact]
    public void SemEntradaNaFila_NaoGuardaArquivoQueJaNaoMudaStatus()
    {
        var root = RepoPaths.FindRepoRoot();
        var stale = SemEntradaNaFila.Keys
            .Where(rel =>
            {
                var full = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
                return !File.Exists(full) || !MudaStatusOuCriaPedido.IsMatch(File.ReadAllText(full));
            })
            .ToList();

        stale.Should().BeEmpty("exceção sem motivo vira buraco: remova de SemEntradaNaFila o arquivo que sumiu ou deixou de mudar status");
    }
}
