using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Lembretes;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.TestHelpers;

namespace EasyStock.Application.Tests.UseCases.Atendimento.Lembretes;

/// <summary>S43, console: criar, concluir e marcar vistos.</summary>
public class LembretesUseCasesTests
{
    private static readonly DateTime Agora = new(2026, 9, 30, 15, 0, 0, DateTimeKind.Utc);

    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly Guid _usuarioId = Guid.NewGuid();
    private readonly ILembreteRepository _repo = Substitute.For<ILembreteRepository>();
    private readonly FakeUnitOfWork _uow = new();
    private readonly FakeTimeProvider _relogio = new(new DateTimeOffset(Agora));

    [Fact]
    public async Task Criar_SemTexto_RecusaSemGravar()
    {
        var act = () => new CriarLembreteUseCase(_repo, _uow, _relogio).ExecuteAsync(
            new CriarLembreteCommand(_empresaId, _usuarioId, " ", null, null, null, null));

        await act.Should().ThrowAsync<UseCaseValidationException>();
        _uow.CommitCount.Should().Be(0);
    }

    [Fact]
    public async Task Criar_GravaManualComQuemCriou()
    {
        var resultado = await new CriarLembreteUseCase(_repo, _uow, _relogio).ExecuteAsync(
            new CriarLembreteCommand(_empresaId, _usuarioId, "Ligar para a Fulana", Agora.AddHours(2), null, null, null));

        resultado.VenceEm.Should().Be(Agora.AddHours(2));
        await _repo.Received(1).AddAsync(Arg.Is<Lembrete>(l => l.CriadoPorUsuarioId == _usuarioId && l.EmpresaId == _empresaId), Arg.Any<CancellationToken>());
        _uow.CommitCount.Should().Be(1);
    }

    [Fact]
    public async Task Concluir_Inexistente_LancaNaoEncontrado()
    {
        var act = () => new ConcluirLembreteUseCase(_repo, _uow, _relogio).ExecuteAsync(_empresaId, _usuarioId, Guid.NewGuid());

        await act.Should().ThrowAsync<LembreteNaoEncontradoException>();
    }

    [Fact]
    public async Task MarcarVistos_SoOsVencidosAindaNaoVistos()
    {
        var vencido = Lembrete.Manual(_empresaId, "vencido", Agora.AddMinutes(-5), _usuarioId, Agora.AddMinutes(-10));
        var futuro = Lembrete.Manual(_empresaId, "futuro", Agora.AddHours(1), _usuarioId, Agora);
        var jaVisto = Lembrete.Manual(_empresaId, "visto", Agora.AddMinutes(-5), _usuarioId, Agora.AddMinutes(-10));
        jaVisto.MarcarVisto(Agora.AddMinutes(-1));
        _repo.ListarAsync(_empresaId, _usuarioId, false, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([vencido, futuro, jaVisto]);

        var marcados = await new MarcarLembretesVistosUseCase(_repo, _uow, _relogio).ExecuteAsync(_empresaId, _usuarioId);

        marcados.Should().Be(1);
        vencido.VistoEm.Should().Be(Agora);
        futuro.VistoEm.Should().BeNull("ainda não apareceu no sininho");
        jaVisto.VistoEm.Should().Be(Agora.AddMinutes(-1));
    }
}
