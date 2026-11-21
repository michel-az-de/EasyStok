using EasyStock.Application.Seguranca;
using EasyStock.Observabilidade;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;

namespace EasyStock.Infra.Async.UnitTests.Logging;

/// <summary>
/// #2390: o log OTLP do console (que não usa Serilog) chega ao exporter com o mesmo mascaramento do span. Roda o
/// pipeline real do ILogger: o <see cref="LogQueRedige"/> e, depois dele, um processador que guarda o que o exporter
/// receberia.
/// </summary>
[Trait("Category", "SecurityRegression")]
public class LogQueRedigeTests
{
    private sealed class Captura : BaseProcessor<LogRecord>
    {
        public string? Mensagem;
        public string? Corpo;
        public List<KeyValuePair<string, object?>> Atributos = [];
        public Exception? Excecao;

        public override void OnEnd(LogRecord data)
        {
            Mensagem = data.FormattedMessage;
            Corpo = data.Body;
            Atributos = data.Attributes?.ToList() ?? [];
            Excecao = data.Exception;
        }
    }

    private static Captura Registrar(Action<ILogger> escrever)
    {
        var captura = new Captura();
        using (var fabrica = LoggerFactory.Create(b => b.AddOpenTelemetry(o =>
               {
                   o.IncludeFormattedMessage = true;
                   o.AddProcessor(new LogQueRedige());
                   o.AddProcessor(captura);
               })))
        {
            escrever(fabrica.CreateLogger("teste"));
        }
        return captura;
    }

    [Fact]
    public void OnEnd_RedigeMensagemEAtributos()
    {
        var captura = Registrar(l => l.LogInformation("Cliente {Telefone} com CPF {Cpf}", "11987654321", "123.456.789-09"));

        captura.Mensagem.Should().NotContain("11987654321").And.NotContain("123.456.789-09");
        captura.Atributos.Select(a => a.Value as string).Should()
            .NotContain(v => v != null && (v.Contains("11987654321") || v.Contains("123.456.789-09")));
        captura.Atributos.Should().Contain(a => a.Key == "Telefone" && (string?)a.Value == SensitivePatterns.RedactedToken);
    }

    [Fact]
    public void OnEnd_PreservaAtributoNaoTexto()
    {
        var captura = Registrar(l => l.LogInformation("Pedido {Id} com {Itens} itens", "abc", 3));

        captura.Atributos.Should().Contain(a => a.Key == "Itens" && Equals(a.Value, 3));
        captura.Mensagem.Should().Be("Pedido abc com 3 itens");
    }

    [Fact]
    public void OnEnd_TrocaExcecaoPorAtributosRedigidos()
    {
        var captura = Registrar(l => l.LogError(new InvalidOperationException("falhou para ana@exemplo.com"), "Erro"));

        captura.Excecao.Should().BeNull("a mensagem da exceção não é editável; o processador a troca por atributos");
        captura.Atributos.Should().Contain(a => a.Key == "exception.type" && (string?)a.Value == typeof(InvalidOperationException).FullName);
        captura.Atributos.Should().Contain(a => a.Key == "exception.message")
            .Which.Value.As<string>().Should().NotContain("ana@exemplo.com");
    }
}
