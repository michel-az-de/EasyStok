using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Services;

namespace EasyStock.Application.UseCases.Atendimento.Inbox;

public sealed record AtendenteResult(Guid UsuarioId, string Nome, string Email);

public sealed record TransferirConversaCommand(Guid EmpresaId, Guid UsuarioId, Guid ConversaId, Guid ParaUsuarioId);

/// <summary>Destino da transferência não é usuário ativo da empresa com permissão de atender: 422.</summary>
public sealed class DestinoNaoAtendenteException(Guid usuarioId)
    : Exception($"O usuário {usuarioId} não atende conversas nesta empresa.")
{
    public Guid UsuarioId { get; } = usuarioId;
}

/// <summary>
/// Quem atende (S41): usuário ativo da empresa cuja permissão efetiva inclui
/// <see cref="Permissao.AtenderConversas"/>. Com mais de um perfil na empresa vale o de maior nível,
/// a mesma escolha do login (<c>AutenticarUsuarioUseCase</c>).
/// </summary>
internal static class RegraAtendente
{
    public static bool Atende(UsuarioDaEmpresa usuario)
    {
        var perfil = usuario.Perfis.OrderBy(p => (int)p.Nivel).FirstOrDefault();
        return perfil is not null && PoliticaPermissao.Tem(perfil.Nivel, perfil.Permissoes, Permissao.AtenderConversas);
    }
}

/// <summary>Lista para o console escolher para quem transferir (S41).</summary>
public sealed class ListarAtendentesUseCase(IAtendenteRepository atendenteRepository)
{
    public async Task<IReadOnlyList<AtendenteResult>> ExecuteAsync(Guid empresaId, CancellationToken ct = default) =>
        (await atendenteRepository.ListarUsuariosAtivosAsync(empresaId, ct))
            .Where(RegraAtendente.Atende)
            .OrderBy(u => u.Nome, StringComparer.CurrentCultureIgnoreCase)
            .Select(u => new AtendenteResult(u.UsuarioId, u.Nome, u.Email))
            .ToList();
}

/// <summary>
/// Passa a conversa para outro atendente (S41). O destino precisa atender nesta empresa. Fica nota
/// interna com quem transferiu e para quem, como na liberação do automático.
/// </summary>
public sealed class TransferirConversaUseCase(
    IConversaRepository conversaRepository,
    IAtendenteRepository atendenteRepository,
    IUnitOfWork unitOfWork)
{
    public const string PrefixoTransferencia = "conversa transferida pelo usuário ";

    public async Task<ConversaSituacaoResult> ExecuteAsync(TransferirConversaCommand command, CancellationToken ct = default)
    {
        var conversa = await conversaRepository.ObterPorIdAsync(command.EmpresaId, command.ConversaId, ct)
            ?? throw new ConversaNaoEncontradaException(command.ConversaId);

        var destino = await atendenteRepository.ObterUsuarioAtivoAsync(command.EmpresaId, command.ParaUsuarioId, ct);
        if (destino is null || !RegraAtendente.Atende(destino))
            throw new DestinoNaoAtendenteException(command.ParaUsuarioId);

        var agora = DateTime.UtcNow;
        conversa.Transferir(destino.UsuarioId, agora);
        await conversaRepository.AddMensagemAsync(
            Mensagem.Saida(command.EmpresaId, conversa.Id, AutorMensagem.Sistema, agora, TipoConteudoMensagem.Texto,
                $"{PrefixoTransferencia}{command.UsuarioId} para {destino.UsuarioId}"),
            ct);
        await unitOfWork.CommitAsync();
        return ConversaSituacaoResult.De(conversa);
    }
}
