using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Domain.Entities.Atendimento;

namespace EasyStock.Application.Tests.Helpers;

/// <summary>
/// #1792: as ações sobre a conversa leem com trava. No dublê, a leitura travada devolve o mesmo que
/// <see cref="IConversaRepository.ObterPorIdAsync"/>; a trava em si é provada no Postgres (CorridasAtendimentoIntegrationTests).
/// </summary>
internal static class ConversaRepositoryMockExtensions
{
    public static IConversaRepository EspelharLeituraComLock(this IConversaRepository conversas)
    {
        conversas.ObterPorIdComLockAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(c => conversas.ObterPorIdAsync(c.ArgAt<Guid>(0), c.ArgAt<Guid>(1), c.ArgAt<CancellationToken>(2)));
        return conversas;
    }

    /// <summary>A conversa aberta do contato também sai da leitura travada (webhooks).</summary>
    public static void DevolverAberta(this IConversaRepository conversas, Conversa aberta) =>
        conversas.ObterPorIdComLockAsync(aberta.EmpresaId, aberta.Id, Arg.Any<CancellationToken>()).Returns(aberta);
}
