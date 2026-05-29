using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace EasyStock.Api.Hosting;

/// <summary>
/// Writer JSON para respostas de <c>MapHealthChecks</c> (UseHealthChecks).
///
/// Formato: <c>{ status, totalDuration, checks: [{ name, status, description, duration, error }] }</c>.
/// Os 4 endpoints da Api (<c>/health</c>, <c>/health/ready</c>, <c>/health/api</c>,
/// <c>/health/dispatcher</c>) usam este writer; <c>/health/live</c> usa o default
/// (Predicate => false).
/// </summary>
public static class HealthCheckResponseWriter
{
    public static Task WriteJsonAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        var result = new
        {
            status = report.Status.ToString(),
            totalDuration = report.TotalDuration.TotalMilliseconds.ToString("0") + "ms",
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
                duration = e.Value.Duration.TotalMilliseconds.ToString("0") + "ms",
                error = e.Value.Exception?.Message
            })
        };
        return context.Response.WriteAsJsonAsync(result, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });
    }
}
