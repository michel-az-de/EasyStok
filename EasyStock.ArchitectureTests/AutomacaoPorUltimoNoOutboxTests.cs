using System.Text.RegularExpressions;
using FluentAssertions;

namespace EasyStock.ArchitectureTests;

/// <summary>
/// Guard (#1506): a automática (S42) manda a mensagem direto ao cliente, sem idempotência. O outbox
/// roda os handlers de um tipo em sequência e, se um handler DEPOIS dela lança, repete o evento inteiro:
/// o cliente recebe a mesma mensagem até o limite de tentativas. Por isso o handler de automática é o
/// último registrado do seu tipo de evento. A ordem de registro é a ordem de execução
/// (<c>GetKeyedServices</c>).
/// </summary>
[Trait("Category", "Architecture")]
public class AutomacaoPorUltimoNoOutboxTests
{
    private static readonly Regex Registro = new(
        @"AddKeyedScoped<[^>]*IIntegrationEventHandler,\s*(?<handler>[\w.]+)>\(\s*(?<tipo>[^)]+?)\s*\);",
        RegexOptions.Compiled | RegexOptions.Singleline);

    [Fact]
    public void HandlerDeAutomacao_EhOUltimoDoSeuTipoDeEvento()
    {
        var root = RepoPaths.FindRepoRoot();
        var fonte = File.ReadAllText(Path.Combine(root,
            "EasyStock.Infra.Postgre", "DependencyInjection", "ServiceCollectionExtensions.cs"));

        var registros = Registro.Matches(fonte)
            .Select(m => (Handler: m.Groups["handler"].Value.Split('.').Last(), Tipo: Normalizar(m.Groups["tipo"].Value)))
            .ToList();
        registros.Should().Contain(r => r.Handler == "AutomacaoPedidoPagoHandler", "o regex precisa achar os registros");

        var violacoes = registros
            .Select((r, i) => (r.Handler, r.Tipo, Indice: i))
            .Where(r => r.Handler.StartsWith("Automacao", StringComparison.Ordinal))
            .Where(a => registros.Skip(a.Indice + 1).Any(d => d.Tipo == a.Tipo && !d.Handler.StartsWith("Automacao", StringComparison.Ordinal)))
            .Select(a => $"{a.Handler} ({a.Tipo})")
            .ToList();

        violacoes.Should().BeEmpty("handler registrado depois da automática pode fazer o outbox reenviar a mensagem ao cliente");
    }

    // "pedido.mudou_status", PedidoPagoEvent.TipoEvento e AutomacaoPedidoEntregueHandler.Tipo apontam para
    // as mesmas strings; normaliza pelas constantes conhecidas.
    private static string Normalizar(string tipo)
    {
        var t = tipo.Trim();
        if (t.EndsWith("PedidoPagoEvent.TipoEvento", StringComparison.Ordinal)) return "pedido.pago";
        if (t.EndsWith("AutomacaoPedidoEntregueHandler.Tipo", StringComparison.Ordinal)) return "pedido.mudou_status";
        return t.Trim('"');
    }
}
