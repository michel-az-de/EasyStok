using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Notifications;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Application.Tests.UseCases.Notifications;

/// <summary>N5: a rotina da empresa criada pela API nasce com os canais pedidos, na ordem de preferência.</summary>
public class CriarRotinaUseCaseTests
{
    private readonly IRotinaRepository _rotinas = Substitute.For<IRotinaRepository>();
    private readonly CriarRotinaUseCase _sut;

    public CriarRotinaUseCaseTests()
    {
        _sut = new CriarRotinaUseCase(_rotinas, Substitute.For<IUnitOfWork>(), NullLogger<CriarRotinaUseCase>.Instance);
    }

    private static CriarRotinaCommand Comando(IReadOnlyList<CanalNotificacao>? canais) => new(
        "resumo_diario_empresa", "Resumo", TipoEventoNotificacao.FaturaVencida, TriggerTipoRotina.Evento,
        "tpl", CategoriaConteudoNotificacao.Operacional, EmpresaId: Guid.NewGuid(), Canais: canais);

    [Fact]
    public async Task AceitaCanaisEOrdem()
    {
        RotinaNotificacao? criada = null;
        await _rotinas.AddAsync(Arg.Do<RotinaNotificacao>(r => criada = r));

        await _sut.ExecuteAsync(Comando([CanalNotificacao.WhatsApp, CanalNotificacao.Email, CanalNotificacao.WhatsApp]));

        criada.Should().NotBeNull();
        criada!.CanaisOrdemFallbackJson.Should().Be("[\"WhatsApp\",\"Email\"]", "a ordem é a de preferência e sem repetição");
    }

    [Fact]
    public async Task SemCanaisMantemARotinaSemCanaisComoHoje()
    {
        RotinaNotificacao? criada = null;
        await _rotinas.AddAsync(Arg.Do<RotinaNotificacao>(r => criada = r));

        await _sut.ExecuteAsync(Comando(null));

        criada!.CanaisOrdemFallbackJson.Should().Be("[]");
    }

    [Theory]
    [InlineData("""{"audiencia":"superadmins"}""")]
    [InlineData("""{"modoCanais":"todos","audiencia":"Superadmins"}""")]
    public async Task AudienciaSuperadminsEmRotinaDaEmpresaEhRecusada(string parametros)
    {
        var comando = Comando(null) with { ParametrosJson = parametros };

        var acao = () => _sut.ExecuteAsync(comando);

        (await acao.Should().ThrowAsync<UseCaseValidationException>()).Which.Code.Should().Be("AUDIENCIA_SUPERADMINS_SO_GLOBAL");
        await _rotinas.DidNotReceiveWithAnyArgs().AddAsync(default!);
    }

    [Theory]
    [InlineData("""{"audiencia":"gestores"}""")]
    [InlineData("""{"modoCanais":"todos"}""")]
    public async Task OutrasAudienciasEmRotinaDaEmpresaSaoAceitas(string parametros)
    {
        await _sut.ExecuteAsync(Comando(null) with { ParametrosJson = parametros });

        await _rotinas.Received(1).AddAsync(Arg.Any<RotinaNotificacao>());
    }

    [Fact]
    public async Task AtualizarTambemRecusaSuperadminsEmRotinaDaEmpresa()
    {
        var empresaId = Guid.NewGuid();
        var rotina = RotinaNotificacao.Criar(
            "r", "R", TipoEventoNotificacao.FaturaVencida, TriggerTipoRotina.Evento, "tpl",
            CategoriaConteudoNotificacao.Operacional, empresaId: empresaId);
        _rotinas.GetByIdAsync(rotina.Id, Arg.Any<CancellationToken>()).Returns(rotina);
        var atualizar = new AtualizarRotinaUseCase(_rotinas, Substitute.For<IUnitOfWork>());

        var acao = () => atualizar.ExecuteAsync(
            new AtualizarRotinaCommand(rotina.Id, null, """{"audiencia":"superadmins"}""", "admin", empresaId));

        await acao.Should().ThrowAsync<UseCaseValidationException>();
        rotina.ParametrosJson.Should().NotContain("superadmins");
    }
}
