using System.Diagnostics;
using EasyStock.Api.Middleware;
using EasyStock.Api.Observability;
using EasyStock.Application.Ports.Output.Observability;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Extensions.Hosting;

namespace EasyStock.Api.UnitTests.Middleware;

/// <summary>
/// #1874: o cliente que fecha a aba ou troca de tela aborta a requisição e o cancelamento sobe como
/// <see cref="OperationCanceledException"/>. Isso não é falha da API: chegava ao log de requisição como
/// erro, "responded 500", e escondia o erro de verdade no ruído.
/// </summary>
public class RequisicaoAbortadaMiddlewareTests
{
    private static DefaultHttpContext Ctx(bool abortada)
    {
        var cts = new CancellationTokenSource();
        if (abortada) cts.Cancel();
        var ctx = new DefaultHttpContext { RequestAborted = cts.Token };
        ctx.Request.Method = "GET";
        ctx.Request.Path = "/api/atendimento/conversas";
        ctx.Response.Body = new MemoryStream();
        return ctx;
    }

    private static RequisicaoAbortadaMiddleware Build(RequestDelegate next) =>
        new(next, NullLogger<RequisicaoAbortadaMiddleware>.Instance);

    public static TheoryData<Exception> CancelamentosDoCliente => new()
    {
        new OperationCanceledException(),
        new TaskCanceledException(),
        new InvalidOperationException("consulta cancelada", new OperationCanceledException()),
        new AggregateException(new OperationCanceledException()),
    };

    [Theory]
    [MemberData(nameof(CancelamentosDoCliente))]
    public async Task Cancelamento_com_a_requisicao_abortada_vira_499_e_nao_sobe(Exception cancelamento)
    {
        var ctx = Ctx(abortada: true);

        await Build(_ => throw cancelamento).InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(499);
    }

    [Fact]
    public async Task Cancelamento_com_a_requisicao_viva_segue_para_o_tratamento_global()
    {
        // Timeout interno, token ligado a outro prazo: não foi o cliente, o tratamento de sempre vale.
        var ctx = Ctx(abortada: false);

        var act = () => Build(_ => throw new OperationCanceledException()).InvokeAsync(ctx);

        await act.Should().ThrowAsync<OperationCanceledException>();
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task Outra_excecao_com_a_requisicao_abortada_continua_subindo()
    {
        var ctx = Ctx(abortada: true);

        var act = () => Build(_ => throw new InvalidOperationException("falha real")).InvokeAsync(ctx);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("falha real");
    }

    [Fact]
    public async Task Resposta_ja_iniciada_nao_tem_o_status_mexido()
    {
        // Mudar o status depois do primeiro byte lança; o cliente já foi embora, só sobra encerrar.
        var ctx = Ctx(abortada: true);
        ctx.Features.Set<IHttpResponseFeature>(new RespostaJaIniciada());

        await Build(_ => throw new OperationCanceledException()).InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task Requisicao_normal_passa_sem_mudar_o_status()
    {
        var ctx = Ctx(abortada: false);

        await Build(c => { c.Response.StatusCode = StatusCodes.Status204NoContent; return Task.CompletedTask; }).InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status204NoContent);
    }

    [Fact]
    public async Task No_pipeline_o_log_da_requisicao_abortada_sai_como_499_e_sem_erro()
    {
        // O pipeline de produção: UseExceptionHandler por fora, o log de requisição do Serilog dentro dele
        // e o middleware logo abaixo do log. Antes, o Serilog via a exceção passar e gravava Error com 500.
        var sink = new ColetorDeEventos();
        var pipeline = MontarPipeline(sink, _ => throw new OperationCanceledException());
        var ctx = Ctx(abortada: true);

        await pipeline(ctx);

        ctx.Response.StatusCode.Should().Be(499);
        var evento = sink.Eventos.Should().ContainSingle(e => e.MessageTemplate.Text.Contains("responded")).Subject;
        evento.Level.Should().BeOneOf(LogEventLevel.Verbose, LogEventLevel.Debug, LogEventLevel.Information);
        evento.Exception.Should().BeNull();
        ((ScalarValue)evento.Properties["StatusCode"]).Value.Should().Be(499);
        sink.Eventos.Should().NotContain(e => e.Level >= LogEventLevel.Error);
    }

    private static RequestDelegate MontarPipeline(ColetorDeEventos sink, RequestDelegate terminal)
    {
        var logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMetrics();
        services.AddSingleton(new DiagnosticListener("Microsoft.AspNetCore"));
        services.AddSingleton(new DiagnosticContext(logger));
        services.AddSingleton(Substitute.For<IOperationalMetrics>());
        services.AddProblemDetails();
        services.AddExceptionHandler<GlobalExceptionHandler>();

        var app = new ApplicationBuilder(services.BuildServiceProvider());
        app.UseExceptionHandler();
        app.UseSerilogRequestLogging(o => o.Logger = logger);
        app.UseMiddleware<RequisicaoAbortadaMiddleware>();
        app.Run(terminal);
        return app.Build();
    }

    private sealed class ColetorDeEventos : ILogEventSink
    {
        public List<LogEvent> Eventos { get; } = [];
        public void Emit(LogEvent logEvent) => Eventos.Add(logEvent);
    }

    private sealed class RespostaJaIniciada : HttpResponseFeature
    {
        public override bool HasStarted => true;
    }
}
