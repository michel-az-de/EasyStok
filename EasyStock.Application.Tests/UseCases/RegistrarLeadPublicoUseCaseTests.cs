using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.Public;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Application.Tests.UseCases;

/// <summary>
/// Cobre RegistrarLeadPublicoUseCase: honeypot, consentimento LGPD,
/// validacao de email, rate-limit por IP, criacao bem-sucedida e
/// path defensivo de email service opcional.
/// </summary>
public class RegistrarLeadPublicoUseCaseTests
{
    private readonly ILeadPublicoRepository _repo = Substitute.For<ILeadPublicoRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private RegistrarLeadPublicoUseCase BuildUseCase(IEmailService? emailService = null) =>
        new(_repo, _uow, NullLogger<RegistrarLeadPublicoUseCase>.Instance, emailService);

    private static RegistrarLeadPublicoCommand CmdValido(
        string? honeypot = null,
        bool consentimento = true,
        string email = "joao@x.com",
        string? ip = null) =>
        new(
            Nome: "Joao Silva",
            Email: email,
            Origem: OrigemLead.TesteGratis,
            ConsentimentoLgpd: consentimento,
            Telefone: "11999998888",
            Empresa: "EasyStok LTDA",
            Mensagem: null,
            TipoNegocio: "Mercadinho",
            ReceberNewsletter: true,
            IpOrigem: ip,
            UserAgent: "Test/1.0",
            UtmSource: null,
            UtmMedium: null,
            UtmCampaign: null,
            Honeypot: honeypot);

    [Fact]
    public async Task Honeypot_preenchido_descarta_lead_sem_persistir()
    {
        var result = await BuildUseCase().ExecuteAsync(CmdValido(honeypot: "bot"));

        result.LeadId.Should().Be(Guid.Empty);
        result.DescartadoPorSpam.Should().BeTrue();
        await _repo.DidNotReceive().AddAsync(Arg.Any<LeadPublico>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Honeypot_apenas_espacos_eh_ignorado_e_lead_eh_persistido()
    {
        var result = await BuildUseCase().ExecuteAsync(CmdValido(honeypot: "   "));

        result.DescartadoPorSpam.Should().BeFalse();
        result.LeadId.Should().NotBe(Guid.Empty);
        await _repo.Received(1).AddAsync(Arg.Any<LeadPublico>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Sem_consentimento_LGPD_lanca_UseCaseValidationException()
    {
        var act = () => BuildUseCase().ExecuteAsync(CmdValido(consentimento: false));

        (await act.Should().ThrowAsync<UseCaseValidationException>())
            .WithMessage("*Consentimento LGPD*");
    }

    [Fact]
    public async Task Email_invalido_lanca_excecao_de_validacao()
    {
        var act = () => BuildUseCase().ExecuteAsync(CmdValido(email: "nao-eh-email"));
        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task Rate_limit_por_IP_descarta_quando_contagem_excede()
    {
        var ip = "10.1.1.1";
        _repo.ContarPorIpRecenteAsync(ip, Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(5); // limite e 5

        var result = await BuildUseCase().ExecuteAsync(CmdValido(ip: ip));

        result.DescartadoPorSpam.Should().BeTrue();
        result.LeadId.Should().Be(Guid.Empty);
        await _repo.DidNotReceive().AddAsync(Arg.Any<LeadPublico>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Lead_persistido_quando_abaixo_do_rate_limit()
    {
        var ip = "10.1.1.2";
        _repo.ContarPorIpRecenteAsync(ip, Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(2);

        var result = await BuildUseCase().ExecuteAsync(CmdValido(ip: ip));

        result.DescartadoPorSpam.Should().BeFalse();
        result.LeadId.Should().NotBe(Guid.Empty);
        await _repo.Received(1).AddAsync(Arg.Any<LeadPublico>(), Arg.Any<CancellationToken>());
        await _uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task Sem_IpOrigem_pula_rate_limit()
    {
        var result = await BuildUseCase().ExecuteAsync(CmdValido(ip: null));

        result.DescartadoPorSpam.Should().BeFalse();
        await _repo.DidNotReceive().ContarPorIpRecenteAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EmailService_opcional_nao_eh_obrigatorio_para_registro_funcionar()
    {
        var result = await BuildUseCase(emailService: null).ExecuteAsync(CmdValido());

        result.LeadId.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task EmailService_provido_eh_invocado_em_background()
    {
        var emailService = Substitute.For<IEmailService>();

        await BuildUseCase(emailService).ExecuteAsync(CmdValido());

        // Email vai em fire-and-forget — damos uma janela curta pra propagar.
        await Task.Delay(100);
        await emailService.Received().SendAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>());
    }
}
