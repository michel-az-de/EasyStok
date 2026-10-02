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
}
