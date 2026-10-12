using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Atendimento.Inbox;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.TestHelpers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Controllers;

public partial class AtendimentoConversasControllerTests
{
    private ReabrirConversaUseCase Reabertura() =>
        new(_repositorio, Substitute.For<IClienteRepository>(), new FakeUnitOfWork(), TimeProvider.System);

    [Fact]
    public async Task Reabrir_ConfirmaFilaHumanaEJanelaContinuaRecusada()
    {
        var conversa = ConversaComClienteAgora(DateTime.UtcNow.AddDays(-2));
        conversa.Encerrar(DateTime.UtcNow.AddDays(-1));

        var result = await _controller.Reabrir(conversa.Id, Reabertura());

        Dados<ConversaSituacaoResult>(result).Situacao.Should().Be(SituacaoConversa.Assumida);
        conversa.AssumidaPorUsuarioId.Should().BeNull();
        var envio = await _controller.EnviarMensagem(conversa.Id, new("oi"));
        envio.Should().BeOfType<ConflictObjectResult>();
        await _canal.DidNotReceiveWithAnyArgs().EnviarTextoAsync(default!, default!, default);
    }

    [Fact]
    public async Task Reabrir_SemPermissao403AntesDeQualquerMudanca()
    {
        var conversa = ConversaComClienteAgora();
        conversa.Encerrar(DateTime.UtcNow);
        _currentUser.TemPermissao(Permissao.AtenderConversas).Returns(false);

        (await _controller.Reabrir(conversa.Id, Reabertura())).Should().BeOfType<ForbidResult>();
        conversa.EstaAberta.Should().BeFalse();
    }

    [Fact]
    public async Task Reabrir_OutraEmpresaOuInexistente404()
    {
        var conversa = Conversa.Abrir(Guid.NewGuid(), WaId, DateTime.UtcNow);
        conversa.Encerrar(DateTime.UtcNow);
        _repositorio.Conversas.Add(conversa);

        (await _controller.Reabrir(conversa.Id, Reabertura())).Should().BeOfType<NotFoundObjectResult>();
        (await _controller.Reabrir(Guid.NewGuid(), Reabertura())).Should().BeOfType<NotFoundObjectResult>();
        conversa.EstaAberta.Should().BeFalse();
    }

    [Fact]
    public async Task Reabrir_OutraAberta409()
    {
        var conversa = ConversaComClienteAgora();
        conversa.Encerrar(DateTime.UtcNow);
        ConversaComClienteAgora();

        (await _controller.Reabrir(conversa.Id, Reabertura())).Should().BeOfType<ConflictObjectResult>();
        conversa.EstaAberta.Should().BeFalse();
    }
}
