using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Admin.VincularWhatsAppTenant;
using Microsoft.Extensions.Configuration;

namespace EasyStock.Application.Tests.UseCases.Admin;

/// <summary>
/// #1102: vínculo do phone_number_id da Meta à empresa pelo back-office. O que estes testes
/// protegem: formato validado antes do banco, número de outra empresa recusado (o webhook roteia
/// por ele, então dois donos misturariam conversas) e <c>null</c> desvinculando.
/// </summary>
public class VincularWhatsAppDoTenantUseCaseTests
{
    private readonly IEmpresaRepository _empresas = Substitute.For<IEmpresaRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly Empresa _empresa = Empresa.Criar("Casa da Baba", "11111111000191");

    public VincularWhatsAppDoTenantUseCaseTests()
    {
        _empresas.GetByIdAsync(_empresa.Id).Returns(_empresa);
    }

    private const string NumeroDaPlataforma = "7770009999";

    private static IConfiguration Config(string? plataforma = NumeroDaPlataforma) =>
        new ConfigurationBuilder().AddInMemoryCollection(
            plataforma is null
                ? []
                : new Dictionary<string, string?> { ["Notifications:WhatsApp:Plataforma:PhoneNumberId"] = plataforma })
            .Build();

    private VincularWhatsAppDoTenantUseCase Sut(string? plataforma = NumeroDaPlataforma) =>
        new(_empresas, _unitOfWork, Config(plataforma));

    [Fact]
    public async Task Vincula_numero_valido_e_persiste()
    {
        var r = await Sut().ExecuteAsync(new VincularWhatsAppDoTenantCommand(_empresa.Id, " 5550001111 "));

        r.Status.Should().Be(StatusVinculoWhatsApp.Vinculado);
        r.PhoneNumberId.Should().Be("5550001111");
        _empresa.WhatsAppPhoneNumberId.Should().Be("5550001111");
        await _empresas.Received(1).UpdateAsync(_empresa);
        await _unitOfWork.Received(1).CommitAsync();
    }

    [Fact]
    public async Task Null_desvincula_e_persiste()
    {
        _empresa.VincularWhatsApp("5550001111");

        var r = await Sut().ExecuteAsync(new VincularWhatsAppDoTenantCommand(_empresa.Id, null));

        r.Status.Should().Be(StatusVinculoWhatsApp.Desvinculado);
        r.PhoneNumberId.Should().BeNull();
        _empresa.WhatsAppPhoneNumberId.Should().BeNull();
        await _unitOfWork.Received(1).CommitAsync();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("55 5000 1111")]
    [InlineData("+5550001111")]
    [InlineData("abc123")]
    [InlineData("123456789012345678901234567890123")] // 33 dígitos: a coluna tem 32
    public async Task Formato_invalido_e_recusado_antes_do_banco(string numero)
    {
        var act = () => Sut().ExecuteAsync(new VincularWhatsAppDoTenantCommand(_empresa.Id, numero));

        await act.Should().ThrowAsync<UseCaseValidationException>();
        await _unitOfWork.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task Empresa_inexistente_devolve_nao_encontrada()
    {
        var r = await Sut().ExecuteAsync(new VincularWhatsAppDoTenantCommand(Guid.NewGuid(), "5550001111"));

        r.Status.Should().Be(StatusVinculoWhatsApp.EmpresaNaoEncontrada);
        await _unitOfWork.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task Numero_de_outra_empresa_e_conflito_sem_persistir()
    {
        var outra = Empresa.Criar("Outra", "22222222000191");
        outra.VincularWhatsApp("5550001111");
        _empresas.GetByWhatsAppPhoneNumberIdAsync("5550001111", Arg.Any<CancellationToken>()).Returns(outra);

        var r = await Sut().ExecuteAsync(new VincularWhatsAppDoTenantCommand(_empresa.Id, "5550001111"));

        r.Status.Should().Be(StatusVinculoWhatsApp.NumeroEmUsoPorOutraEmpresa);
        _empresa.WhatsAppPhoneNumberId.Should().BeNull();
        await _unitOfWork.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task Mesmo_numero_na_mesma_empresa_nao_e_conflito()
    {
        _empresa.VincularWhatsApp("5550001111");
        _empresas.GetByWhatsAppPhoneNumberIdAsync("5550001111", Arg.Any<CancellationToken>()).Returns(_empresa);

        var r = await Sut().ExecuteAsync(new VincularWhatsAppDoTenantCommand(_empresa.Id, "5550001111"));

        r.Status.Should().Be(StatusVinculoWhatsApp.Vinculado);
    }

    [Fact]
    public async Task NumeroDaPlataformaNaoVinculaAEmpresa()
    {
        // N6: o número de plataforma nunca tem dono; senão o webhook do atendimento criaria Conversa para ele.
        var r = await Sut().ExecuteAsync(new VincularWhatsAppDoTenantCommand(_empresa.Id, $" {NumeroDaPlataforma} "));

        r.Status.Should().Be(StatusVinculoWhatsApp.NumeroReservadoDaPlataforma);
        _empresa.WhatsAppPhoneNumberId.Should().BeNull();
        await _empresas.DidNotReceive().UpdateAsync(Arg.Any<Empresa>());
        await _unitOfWork.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task SemNumeroDePlataformaConfiguradoOVinculoSegueIgual()
    {
        var r = await Sut(plataforma: null).ExecuteAsync(new VincularWhatsAppDoTenantCommand(_empresa.Id, NumeroDaPlataforma));

        r.Status.Should().Be(StatusVinculoWhatsApp.Vinculado);
    }
}
