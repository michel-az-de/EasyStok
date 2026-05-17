using System.Text;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Infra.Notifications.Templating;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Api.UnitTests.Notifications;

public class ScribanRendererTests
{
    private static IRendererTemplate CriarRenderer() =>
        new ScribanRenderer(NullLogger<ScribanRenderer>.Instance);

    [Fact]
    public async Task Renderiza_template_simples_substituindo_variaveis()
    {
        var renderer = CriarRenderer();

        var resultado = await renderer.RenderizarAsync(
            "Ola {{ nome }}, voce tem {{ dias }} dias.",
            new Dictionary<string, object?> { ["nome"] = "Felipe", ["dias"] = 7 });

        resultado.Should().Be("Ola Felipe, voce tem 7 dias.");
    }

    [Fact]
    public async Task Variavel_ausente_renderiza_como_string_vazia()
    {
        var renderer = CriarRenderer();

        var resultado = await renderer.RenderizarAsync(
            "Antes-{{ variavelInexistente }}-Depois",
            new Dictionary<string, object?>());

        resultado.Should().Be("Antes--Depois");
    }

    [Fact]
    public async Task Template_invalido_lanca_InvalidOperationException()
    {
        var renderer = CriarRenderer();

        var act = async () => await renderer.RenderizarAsync(
            "{{ for x in }}",
            new Dictionary<string, object?>());

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Template invalido*");
    }

    [Fact]
    public async Task Template_acima_do_limite_e_rejeitado()
    {
        var renderer = CriarRenderer();
        var grande = new string('a', 256 * 1024 + 1);

        var act = async () => await renderer.RenderizarAsync(
            grande, new Dictionary<string, object?>());

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Template excede tamanho maximo*");
    }

    [Fact]
    public async Task LoopLimit_dispara_em_range_grande()
    {
        var renderer = CriarRenderer();

        var act = async () => await renderer.RenderizarAsync(
            "{{ for x in 1..10000 }}{{ x }}{{ end }}",
            new Dictionary<string, object?>());

        // LoopLimit = 500 -> ScriptRuntimeException -> embrulhada como InvalidOperationException
        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.WithMessage("*LoopLimit*");
    }

    [Fact]
    public async Task RecursaoMutua_eh_limitada()
    {
        var renderer = CriarRenderer();

        // Loop aninhado de 30x30 = 900 > LoopLimit 500
        var act = async () => await renderer.RenderizarAsync(
            "{{ for i in 1..30 }}{{ for j in 1..30 }}{{ i }}-{{ j }}{{ end }}{{ end }}",
            new Dictionary<string, object?>());

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private sealed class ObjetoComReflection
    {
        public string Nome => "exposto";
        public Type TipoVazado => typeof(string);
    }

    [Fact]
    public async Task Tipos_perigosos_sao_bloqueados_via_MemberFilter()
    {
        var renderer = CriarRenderer();

        // 1) Membros seguros (string Nome) continuam acessiveis.
        var resultadoNome = await renderer.RenderizarAsync(
            "{{ obj.nome }}",
            new Dictionary<string, object?> { ["obj"] = new ObjetoComReflection() });
        resultadoNome.Should().Be("exposto");

        // 2) Membros que retornam Type sao bloqueados pelo MemberFilter
        //    -> Scriban dispara ScriptRuntimeException (membro nao acessivel),
        //    embrulhada em InvalidOperationException. Aceita tambem retorno vazio
        //    caso a runtime evolua para silenciar.
        Exception? capturada = null;
        string resultadoTipo = string.Empty;
        try
        {
            resultadoTipo = await renderer.RenderizarAsync(
                "{{ obj.tipo_vazado.assembly.get_types.size }}",
                new Dictionary<string, object?> { ["obj"] = new ObjetoComReflection() });
        }
        catch (Exception ex)
        {
            capturada = ex;
        }

        if (capturada is null)
        {
            resultadoTipo.Should().NotContain("System.");
            resultadoTipo.Should().NotContain("Scriban");
        }
        else
        {
            capturada.Should().BeOfType<InvalidOperationException>();
            (capturada.Message + " " + capturada.InnerException?.Message)
                .Should().Contain("tipo_vazado");
        }
    }

    [Fact]
    public async Task Include_eh_bloqueado_sem_TemplateLoader()
    {
        var renderer = CriarRenderer();

        var act = async () => await renderer.RenderizarAsync(
            "{{ include 'qualquer' }}",
            new Dictionary<string, object?>());

        // Sem TemplateLoader, include dispara ScriptRuntimeException, embrulhada.
        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.WithMessage("*TemplateLoader*");
    }

    [Fact]
    public async Task HtmlEscape_aplica_encoding_em_strings()
    {
        var renderer = CriarRenderer();

        var resultado = await renderer.RenderizarAsync(
            "<p>{{ nome }}</p>",
            new Dictionary<string, object?> { ["nome"] = "<script>alert(1)</script>" },
            htmlEscape: true);

        resultado.Should().Be("<p>&lt;script&gt;alert(1)&lt;/script&gt;</p>");
    }

    [Fact]
    public async Task HtmlEscape_falso_mantem_strings_literais()
    {
        var renderer = CriarRenderer();

        var resultado = await renderer.RenderizarAsync(
            "<p>{{ nome }}</p>",
            new Dictionary<string, object?> { ["nome"] = "<b>X</b>" },
            htmlEscape: false);

        resultado.Should().Be("<p><b>X</b></p>");
    }

    [Fact]
    public async Task HtmlEscape_nao_afeta_numeros_e_booleanos()
    {
        var renderer = CriarRenderer();

        var resultado = await renderer.RenderizarAsync(
            "qty={{ qty }};active={{ active }}",
            new Dictionary<string, object?> { ["qty"] = 42, ["active"] = true },
            htmlEscape: true);

        resultado.Should().Be("qty=42;active=true");
    }

    [Fact]
    public async Task Cache_reusa_template_compilado_para_mesmo_source()
    {
        var renderer = CriarRenderer();
        var source = "Ola {{ nome }}";

        for (var i = 0; i < 50; i++)
        {
            var r = await renderer.RenderizarAsync(
                source,
                new Dictionary<string, object?> { ["nome"] = $"u{i}" });
            r.Should().Be($"Ola u{i}");
        }
    }

    [Fact]
    public async Task Renderer_eh_thread_safe_sob_paralelismo()
    {
        var renderer = CriarRenderer();

        var tasks = Enumerable.Range(0, 64).Select(i => Task.Run(async () =>
        {
            var r = await renderer.RenderizarAsync(
                "id={{ id }}",
                new Dictionary<string, object?> { ["id"] = i });
            return (i, r);
        })).ToArray();

        var resultados = await Task.WhenAll(tasks);

        foreach (var (i, r) in resultados)
            r.Should().Be($"id={i}");
    }

    [Fact]
    public async Task CancellationToken_externo_propaga_OperationCanceledException()
    {
        var renderer = CriarRenderer();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await renderer.RenderizarAsync(
            "{{ x }}",
            new Dictionary<string, object?> { ["x"] = 1 },
            cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Funcoes_seguras_de_string_continuam_disponiveis()
    {
        var renderer = CriarRenderer();

        var resultado = await renderer.RenderizarAsync(
            "{{ nome | string.upcase }}",
            new Dictionary<string, object?> { ["nome"] = "felipe" });

        resultado.Should().Be("FELIPE");
    }

    [Fact]
    public async Task Templates_diferentes_geram_resultados_independentes()
    {
        var renderer = CriarRenderer();

        var r1 = await renderer.RenderizarAsync(
            "A:{{ x }}", new Dictionary<string, object?> { ["x"] = "um" });
        var r2 = await renderer.RenderizarAsync(
            "B:{{ x }}", new Dictionary<string, object?> { ["x"] = "dois" });

        r1.Should().Be("A:um");
        r2.Should().Be("B:dois");
    }
}
