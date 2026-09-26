using EasyStock.Api.Controllers;
using EasyStock.Api.Http;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Atendimento;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Notifications.Options;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Controllers;

public class IntegracoesWhatsAppControllerTests
{
    private readonly ITenantFeatureFlagRepository _featureFlagRepository = Substitute.For<ITenantFeatureFlagRepository>();
    private readonly IEmpresaRepository _empresaRepository = Substitute.For<IEmpresaRepository>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly IntegracoesWhatsAppController _controller;

    public IntegracoesWhatsAppControllerTests()
    {
        var useCase = new ObterStatusIntegracaoWhatsAppUseCase(_featureFlagRepository, _empresaRepository);
        var metaOptions = Options.Create(new MetaCloudWhatsAppOptions { ApiVersion = "v19.0" });
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Notifications:WhatsApp:Provider"] = "meta" })
            .Build();

        _controller = new IntegracoesWhatsAppController(useCase, metaOptions, configuration, _currentUser);
    }

    [Fact]
    public async Task SemFlag404()
    {
        var empresaId = Guid.NewGuid();
        _currentUser.EmpresaId.Returns(empresaId);
        _featureFlagRepository.ListarAtivasAsync(empresaId, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<string>());

        var result = await _controller.GetStatus(CancellationToken.None);

        result.Should().BeOfType<NotFoundObjectResult>();
        var notFound = (NotFoundObjectResult)result;
        notFound.Value.Should().BeOfType<ApiErrorResponse>()
            .Subject.Error.Code.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task ComFlagDevolveStatus()
    {
        var empresaId = Guid.NewGuid();
        _currentUser.EmpresaId.Returns(empresaId);
        _featureFlagRepository.ListarAtivasAsync(empresaId, Arg.Any<CancellationToken>())
            .Returns(new[] { "modulo.atendimento" });

        var empresa = Empresa.Criar("Casa da Baba", "11111111000191");
        empresa.VincularWhatsApp("551199998888");
        _empresaRepository.GetByIdAsync(empresaId).Returns(empresa);

        var result = await _controller.GetStatus(CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        var ok = (OkObjectResult)result;
        var dataProp = ok.Value!.GetType().GetProperty("Data");
        var data = dataProp!.GetValue(ok.Value);
        data!.GetType().GetProperty("phoneNumberId")!.GetValue(data).Should().Be("551199998888");
        data.GetType().GetProperty("apiVersion")!.GetValue(data).Should().Be("v19.0");
        data.GetType().GetProperty("provider")!.GetValue(data).Should().Be("meta");
    }
}
