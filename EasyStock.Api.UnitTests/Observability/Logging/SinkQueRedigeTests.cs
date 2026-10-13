using EasyStock.Infra.Async.Logging;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace EasyStock.Api.UnitTests.Observability.Logging;

/// <summary>
/// I4.5 (#1698): o segredo-canário não sai por nenhum sink. O invólucro <see cref="SinkQueRedige"/> fica na
/// frente de console, arquivo e OTLP nos dois hosts; aqui o sink interno é um coletor, que vê exatamente o
/// evento que qualquer sink real receberia.
/// </summary>
[Trait("Category", "SecurityRegression")]
public class SinkQueRedigeTests
{
    private const string Canario = "CANARIO-9f3a7c21d4";

    private sealed class Coletor : ILogEventSink
    {
        public List<LogEvent> Eventos { get; } = [];
        public void Emit(LogEvent logEvent) => Eventos.Add(logEvent);
    }

    private static string Tudo(LogEvent e) =>
        e.MessageTemplate.Text + "|" + e.RenderMessage() + "|"
        + string.Join("|", e.Properties.Select(p => p.Key + "=" + p.Value)) + "|" + e.Exception;

    [Fact]
    public void Canario_ausente_de_todos_os_sinks()
    {
        var coletor = new Coletor();
        using (var logger = new LoggerConfiguration().WriteTo.Redigido(w => w.Sink(coletor)).CreateLogger())
        {
            logger.Information("marca-1 propriedade com nome sensivel {AccessToken}", Canario);
            logger.Information("marca-2 corpo do provedor {Corpo}", "{\"access_token\":\"" + Canario + "\",\"status\":401}");
            logger.Information("marca-3 chamada {Url}", "https://api.tomtom.com/search/2/geocode/x.json?limit=1&key=" + Canario);
            logger.Information("marca-4 cabecalho {Cabecalho}", "Bearer " + Canario);
            logger.Information("marca-5 objeto {@Conexao}", new { Provedor = "mp", Senha = Canario, Itens = new[] { "token=" + Canario } });
            logger.Information("marca-6 mapa {Mapa}", new Dictionary<string, string> { ["api_key"] = Canario });
            logger.Error(new InvalidOperationException("MP respondeu 401: {\"access_token\":\"" + Canario + "\"}"), "marca-7 falha");
            logger.Information("marca-8 texto fixo token=" + Canario);
        }

        coletor.Eventos.Should().HaveCount(8);
        var saida = string.Join("\n", coletor.Eventos.Select(Tudo));
        saida.Should().NotContain(Canario);
        saida.Should().Contain("marca-1").And.Contain("marca-8").And.Contain("[REDACTED]");
        saida.Should().Contain("api.tomtom.com", "so o segredo some; o resto do evento continua util");
    }

    [Fact]
    public void Excecao_redigida_guarda_o_tipo_original()
    {
        var coletor = new Coletor();
        using (var logger = new LoggerConfiguration().WriteTo.Redigido(w => w.Sink(coletor)).CreateLogger())
            logger.Error(new HttpRequestException("corpo: token=" + Canario), "falha");

        var excecao = coletor.Eventos.Single().Exception;
        excecao.Should().BeOfType<ExcecaoRedigida>().Which.TipoOriginal.Should().Be(typeof(HttpRequestException).FullName);
        excecao!.ToString().Should().StartWith("System.Net.Http.HttpRequestException").And.NotContain(Canario);
        excecao.Message.Should().NotContain(Canario);
    }

    [Fact]
    public void Evento_sem_segredo_passa_intacto()
    {
        var coletor = new Coletor();
        var original = new InvalidOperationException("pedido nao encontrado");
        using (var logger = new LoggerConfiguration().WriteTo.Redigido(w => w.Sink(coletor)).CreateLogger())
            logger.Warning(original, "Pedido {PedidoId} em {Elapsed} ms", 42, 1.5);

        var evento = coletor.Eventos.Single();
        evento.Exception.Should().BeSameAs(original);
        evento.RenderMessage().Should().Be("Pedido 42 em 1.5 ms");
        evento.Properties["PedidoId"].Should().BeOfType<ScalarValue>().Which.Value.Should().Be(42);
    }

    [Fact]
    public void Contagem_de_tokens_e_nome_de_variavel_nao_sao_segredo()
    {
        // Diagnóstico da chave-mestra (I4.1) e consumo de IA: nome parecido, valor que precisa aparecer.
        var coletor = new Coletor();
        using (var logger = new LoggerConfiguration().WriteTo.Redigido(w => w.Sink(coletor)).CreateLogger())
            logger.Information("IA {TokensEntrada}/{TokensSaida}; falta {VarKek} ({KekId})", 1200, 300, "EZ_CRYPTO_KEK", "kek-2026");

        coletor.Eventos.Single().RenderMessage().Should().Be("IA 1200/300; falta \"EZ_CRYPTO_KEK\" (\"kek-2026\")");
    }

    [Fact]
    public void Configuracao_por_json_redige_o_sink_interno()
    {
        // Mesmo formato do appsettings dos hosts: Redigido envolvendo o sink de verdade.
        var arquivo = Path.Combine(Path.GetTempPath(), $"easystock-redigido-{Guid.NewGuid():N}.log");
        var configuracao = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Serilog:Using:0"] = "Serilog.Sinks.File",
            ["Serilog:Using:1"] = "EasyStock.Infra.Async",
            ["Serilog:WriteTo:0:Name"] = "Redigido",
            ["Serilog:WriteTo:0:Args:configure:0:Name"] = "File",
            ["Serilog:WriteTo:0:Args:configure:0:Args:path"] = arquivo,
        }).Build();

        try
        {
            using (var logger = new LoggerConfiguration().ReadFrom.Configuration(configuracao).CreateLogger())
                logger.Information("marca-json {Corpo}", "{\"access_token\":\"" + Canario + "\"}");

            var texto = File.ReadAllText(arquivo);
            texto.Should().Contain("marca-json").And.NotContain(Canario);
        }
        finally
        {
            File.Delete(arquivo);
        }
    }
}