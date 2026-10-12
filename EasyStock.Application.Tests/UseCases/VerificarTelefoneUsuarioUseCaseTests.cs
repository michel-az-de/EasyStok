using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.ContatoUsuario;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Domain.ValueObjects;
using EasyStock.TestHelpers;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.UseCases;

/// <summary>N4: verificação administrativa do telefone (SuperAdmin), com motivo, consentimentos de WhatsApp e auditoria.</summary>
public class VerificarTelefoneUsuarioUseCaseTests
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 2, 17, 30, 0, TimeSpan.Zero);

    private readonly IUsuarioRepository _usuarios = Substitute.For<IUsuarioRepository>();
    private readonly IConsentimentoRepository _consentimentos = Substitute.For<IConsentimentoRepository>();
    private readonly IAuditLogRepository _auditoria = Substitute.For<IAuditLogRepository>();
    private readonly ICurrentUserAccessor _atual = Substitute.For<ICurrentUserAccessor>();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly Guid _superAdminId = Guid.NewGuid();
    private readonly Usuario _dona;

    public VerificarTelefoneUsuarioUseCaseTests()
    {
        _atual.UsuarioId.Returns(_superAdminId);
        _atual.Ip.Returns("203.0.113.9");
        _dona = Usuario.Criar("Dona", "dona@casadababa.com", "hash");
        _dona.DefinirTelefone(TelefoneE164.From("11997573992"));
        _usuarios.GetByIdAsync(_dona.Id).Returns(_dona);
    }

    private VerificarTelefoneUsuarioUseCase Criar() => new(
        _usuarios, _consentimentos, _auditoria, _atual, _unitOfWork, new FakeTimeProvider(Agora),
        Substitute.For<ILogger<VerificarTelefoneUsuarioUseCase>>());

    [Fact]
    public async Task VerificaGravaConsentimentosEAudita()
    {
        var gravados = new List<ConsentimentoNotificacao>();
        await _consentimentos.AddAsync(Arg.Do<ConsentimentoNotificacao>(gravados.Add));

        var r = await Criar().ExecuteAsync(new VerificarTelefoneUsuarioCommand(_dona.Id, "Confirmado por ligacao com a dona"));

        _dona.TelefoneVerificadoEm.Should().Be(Agora.UtcDateTime);
        r.Telefone.Should().Be("+5511997573992");
        gravados.Should().HaveCount(2);
        gravados.Select(c => c.Categoria).Should().BeEquivalentTo(
            [CategoriaConteudoNotificacao.Seguranca, CategoriaConteudoNotificacao.Operacional]);
        gravados.Should().OnlyContain(c =>
            c.Canal == CanalNotificacao.WhatsApp && c.OptIn && c.UsuarioId == _dona.Id
            && c.AtualizadoPor == $"superadmin:{_superAdminId}" && c.IpOrigem == "203.0.113.9");
        await _auditoria.Received(1).AddAsync(Arg.Is<AuditLog>(a =>
            a.Acao == "telefone-verificado" && a.UsuarioId == _dona.Id && a.Detalhes!.Contains("Confirmado por ligacao")));
        _unitOfWork.CommitCount.Should().Be(1);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("curto")]
    [InlineData("         ")]
    public async Task MotivoCurtoRecusa(string? motivo)
    {
        var acao = () => Criar().ExecuteAsync(new VerificarTelefoneUsuarioCommand(_dona.Id, motivo));

        (await acao.Should().ThrowAsync<UseCaseValidationException>()).Which.Code.Should().Be("MOTIVO_OBRIGATORIO");
        _dona.TelefoneVerificadoEm.Should().BeNull();
        _unitOfWork.CommitCount.Should().Be(0);
    }

    [Fact]
    public async Task UsuarioSemTelefoneOuInativoRecusa()
    {
        var semTelefone = Usuario.Criar("Sem", "sem@casadababa.com", "hash");
        _usuarios.GetByIdAsync(semTelefone.Id).Returns(semTelefone);

        var acao = () => Criar().ExecuteAsync(new VerificarTelefoneUsuarioCommand(semTelefone.Id, "Motivo suficiente aqui"));

        (await acao.Should().ThrowAsync<UseCaseValidationException>()).Which.Code.Should().Be("TELEFONE_AUSENTE");

        _dona.Ativo = false;
        var inativo = () => Criar().ExecuteAsync(new VerificarTelefoneUsuarioCommand(_dona.Id, "Motivo suficiente aqui"));
        await inativo.Should().ThrowAsync<UseCaseValidationException>();
        await _consentimentos.DidNotReceiveWithAnyArgs().AddAsync(default!);
    }

    [Fact]
    public async Task UsuarioInexistenteRecusa()
    {
        var acao = () => Criar().ExecuteAsync(new VerificarTelefoneUsuarioCommand(Guid.NewGuid(), "Motivo suficiente aqui"));

        (await acao.Should().ThrowAsync<UseCaseValidationException>()).Which.Code.Should().Be("USUARIO_NAO_ENCONTRADO");
    }
}
