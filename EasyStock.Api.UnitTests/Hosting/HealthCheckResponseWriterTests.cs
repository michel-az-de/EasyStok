using System.Text.Json;
using EasyStock.Api.Hosting;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Hosting;

/// <summary>#2401: /health* é público; descrição e mensagem de exceção (host, string de conexão) só em Development.</summary>
public class HealthCheckResponseWriterTests
{
    private static async Task<JsonElement> EscreverAsync(string ambiente)
    {
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns(ambiente);
        var context = new DefaultHttpContext { RequestServices = new ServiceCollection().AddSingleton(env).BuildServiceProvider() };
        context.Response.Body = new MemoryStream();
        var report = new HealthReport(new Dictionary<string, HealthReportEntry>
        {
            ["db"] = new(HealthStatus.Unhealthy, "Host=10.0.0.5;Password=segredo", TimeSpan.FromMilliseconds(5),
                new InvalidOperationException("falha em 10.0.0.5:5432"), null)
        }, TimeSpan.FromMilliseconds(5));

        await HealthCheckResponseWriter.WriteJsonAsync(context, report);

        context.Response.Body.Position = 0;
        return (await JsonDocument.ParseAsync(context.Response.Body)).RootElement.GetProperty("checks")[0];
    }

    [Fact]
    public async Task EmProducao_NaoExpoeDescricaoNemErro()
    {
        var check = await EscreverAsync(Environments.Production);

        check.GetProperty("name").GetString().Should().Be("db");
        check.GetProperty("status").GetString().Should().Be("Unhealthy");
        check.GetProperty("description").ValueKind.Should().Be(JsonValueKind.Null);
        check.GetProperty("error").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task EmDevelopment_ExpoDescricaoEErro()
    {
        var check = await EscreverAsync(Environments.Development);

        check.GetProperty("description").GetString().Should().Contain("Host=");
        check.GetProperty("error").GetString().Should().Contain("falha em");
    }
}
