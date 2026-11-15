using EasyStock.Api.Controllers;
using EasyStock.Api.Services.Relatorios;
using EasyStock.Application.Ports.Output.Storage;
using EasyStock.Domain.Reporting;
using EasyStock.Infra.Async.Storage;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;
using System.Reflection;

namespace EasyStock.Api.UnitTests.Relatorios;

/// <summary>
/// #2668: no storage local o link do artefato é um token do Data Protection com validade, que só a API decifra.
/// Antes era base64 da chave, sem assinatura, para uma rota que não existia.
/// </summary>
[Trait("Category", "SecurityRegression")]
public class RelatorioDownloadLinkTests
{
    private const string Chave = "tenants/empresa/reports/2026/11/run.csv";
    private readonly EphemeralDataProtectionProvider _dp = new();

    private static LocalFileStorage Local() =>
        new(Options.Create(new FileStorageOptions()), Substitute.For<IHostEnvironment>());

    private static ReportRun Run(Guid empresa, string chave = Chave)
    {
        var run = ReportRun.Enqueue(empresa, Guid.NewGuid(), "vendas.por-periodo", ReportCategoria.Vendas,
            ReportContexto.Tenant, "{}", "hash", ReportFormat.Csv, "1.0.0", TimeSpan.FromDays(7));
        run.TryStart(TimeSpan.FromMinutes(5));
        run.MarkSucceeded(chave, 10, "sha", 1);
        return run;
    }

    private static string Token(Uri link) => link.ToString()[(RelatorioDownloadLink.Rota.Length + 1)..];

    [Fact]
    public async Task Token_volta_com_empresa_run_e_chave()
    {
        var link = new RelatorioDownloadLink(_dp, Local());
        var empresa = Guid.NewGuid();
        var run = Run(empresa);

        var uri = await link.CriarAsync(run, "x.csv", TimeSpan.FromMinutes(15), default);

        uri.IsAbsoluteUri.Should().BeFalse();
        uri.ToString().Should().StartWith(RelatorioDownloadLink.Rota + "/").And.NotContain("tenants");
        link.Validar(Token(uri)).Should().Be(new Application.Ports.Output.Reporting.RelatorioDownloadAutorizado(empresa, run.Id, Chave));
    }

    [Fact]
    public async Task Token_adulterado_vencido_ou_de_outro_proposito_nao_vale()
    {
        var link = new RelatorioDownloadLink(_dp, Local());
        var run = Run(Guid.NewGuid());
        var token = Token(await link.CriarAsync(run, "x.csv", TimeSpan.FromMinutes(15), default));
        var vencido = Token(await link.CriarAsync(run, "x.csv", TimeSpan.FromMinutes(-1), default));
        var outroProposito = _dp.CreateProtector("outro").ToTimeLimitedDataProtector()
            .Protect($"{Guid.NewGuid():N}|{run.Id:N}|{Chave}", DateTimeOffset.UtcNow.AddMinutes(15));
        var trocado = token[^2] == 'A' ? 'B' : 'A';

        link.Validar(token[..^2] + trocado + token[^1..]).Should().BeNull();
        link.Validar(vencido).Should().BeNull();
        link.Validar(outroProposito).Should().BeNull();
        link.Validar("nao-e-token").Should().BeNull();
        link.Validar(null).Should().BeNull();
        link.Validar(" ").Should().BeNull();
    }

    [Fact]
    public async Task Token_de_outra_chave_de_cifra_nao_vale()
    {
        var run = Run(Guid.NewGuid());
        var token = Token(await new RelatorioDownloadLink(_dp, Local()).CriarAsync(run, "x.csv", TimeSpan.FromMinutes(15), default));

        new RelatorioDownloadLink(new EphemeralDataProtectionProvider(), Local()).Validar(token).Should().BeNull();
    }

    [Fact]
    public async Task Com_S3_delega_para_a_url_pre_assinada_do_provedor()
    {
        var s3 = Substitute.For<IFileStorage>();
        var esperado = new Uri("https://bucket.example.test/run.csv?assinatura=1");
        s3.CreatePreSignedDownloadUrlAsync(Chave, TimeSpan.FromMinutes(15), "x.csv", Arg.Any<CancellationToken>()).Returns(esperado);

        var uri = await new RelatorioDownloadLink(_dp, s3).CriarAsync(Run(Guid.NewGuid()), "x.csv", TimeSpan.FromMinutes(15), default);

        uri.Should().Be(esperado);
    }

    [Fact]
    public void Endpoint_e_anonimo_e_tem_limite_de_taxa_por_ip()
    {
        var tipo = typeof(ReportArtefatosController);

        tipo.GetCustomAttribute<AllowAnonymousAttribute>().Should().NotBeNull("o link abre numa aba nova, sem JWT");
        tipo.GetCustomAttribute<EnableRateLimitingAttribute>()!.PolicyName.Should().Be("mobile-anonymous");
    }
}
