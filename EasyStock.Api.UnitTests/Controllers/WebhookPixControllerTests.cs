using System.Text;
using EasyStock.Api.Controllers;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Financeiro.Pagamentos;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Controllers;

/// <summary>#1508: o webhook Pix não repassava o CancellationToken da requisição para a reconciliação.</summary>
public class WebhookPixControllerTests
{
    [Fact]
    public async Task Pix_RepassaOCancellationTokenDaRequisicaoParaAReconciliacao()
    {
        var contas = Substitute.For<IContaReceberRepository>();
        var reconciliar = new ReconciliarPixParcelaReceberUseCase(contas, Substitute.For<ICaixaRepository>(),
            Substitute.For<IEfiPixService>(), Substitute.For<IUnitOfWork>(),
            NullLogger<ReconciliarPixParcelaReceberUseCase>.Instance);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Efi:WebhookAllowUnsigned"] = "true",
        }).Build();
        var ambiente = Substitute.For<IWebHostEnvironment>();
        ambiente.EnvironmentName.Returns("Development");
        var http = new DefaultHttpContext();
        http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{\"pix\":[{\"txid\":\"cr-123\"}]}"));
        var controller = new WebhookPixController(config, reconciliar, NullLogger<WebhookPixController>.Instance, ambiente)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
        };
        using var cts = new CancellationTokenSource();

        var result = await controller.Pix(cts.Token);

        result.Should().BeOfType<OkResult>();
        await contas.Received(1).GetParcelaByEfiTxidAsync("cr-123", cts.Token);
    }
}
