using EasyStock.Application.Services.Auth;
using EasyStock.Domain.Exceptions.Storefront;
using EasyStock.Domain.ValueObjects;

namespace EasyStock.Application.UseCases.ContatoUsuario;

public sealed record DefinirMeuTelefoneCommand(string? Telefone, string? SenhaAtual) : ICommand;

/// <param name="Telefone">O telefone gravado, em E.164.</param>
/// <param name="Verificado">Sempre falso depois da troca: o WhatsApp só recebe depois da verificação.</param>
public sealed record DefinirMeuTelefoneResult(string Telefone, bool Verificado);

/// <summary>
/// O próprio usuário define o telefone (N4), com a senha atual. Telefone novo zera a verificação, avisa o e-mail atual
/// e derruba as sessões (N7). O mesmo número de novo não faz nada: nem aviso, nem logout, nem perde a verificação.
/// </summary>
public sealed class DefinirMeuTelefoneUseCase(
    IUsuarioRepository usuarioRepository,
    ICurrentUserAccessor currentUserAccessor,
    TrocaDeContatoService trocaDeContato,
    RevogadorSessoes revogadorSessoes,
    IUnitOfWork unitOfWork,
    ILogger<DefinirMeuTelefoneUseCase> logger) : IUseCase<DefinirMeuTelefoneCommand, DefinirMeuTelefoneResult>
{
    public async Task<DefinirMeuTelefoneResult> ExecuteAsync(DefinirMeuTelefoneCommand command)
    {
        var usuarioId = currentUserAccessor.UsuarioId;
        if (usuarioId == Guid.Empty)
            throw new UsuarioNaoAutorizadoException("Usuario nao autenticado.");

        var usuario = await usuarioRepository.GetByIdAsync(usuarioId)
            ?? throw new RegraDeDominioVioladaException("Usuario nao encontrado.");

        await trocaDeContato.ExigirSenhaAtualAsync(usuario, command.SenhaAtual);

        TelefoneE164 telefone;
        try
        {
            telefone = TelefoneE164.From(command.Telefone);
        }
        catch (TelefoneInvalidoException ex)
        {
            throw new UseCaseValidationException("TELEFONE_INVALIDO", ex.Message);
        }

        if (usuario.Telefone == telefone)
            return new DefinirMeuTelefoneResult(telefone.Value, usuario.TelefoneVerificadoEm is not null);

        var empresaId = await trocaDeContato.ResolverEmpresaDoEventoAsync();
        usuario.DefinirTelefone(telefone);
        await usuarioRepository.UpdateAsync(usuario);

        // O aviso vai para o e-mail atual com o telefone novo mascarado: quem não pediu a troca fica sabendo.
        await trocaDeContato.AvisarContatoAlteradoAsync(
            usuario, usuario.Email, "telefone", TrocaDeContatoService.MascararTelefone(telefone.Value), empresaId);

        // N7: telefone trocado derruba as sessões (inclusive esta), como a troca de senha. Antes do commit: o pior caso é o
        // usuário deslogado sem a troca ter acontecido, nunca a troca sem o logout.
        await revogadorSessoes.RevogarAsync(usuario);
        await unitOfWork.CommitAsync();

        logger.LogInformation("Telefone do usuario {UsuarioId} alterado; verificacao zerada", usuario.Id);
        return new DefinirMeuTelefoneResult(telefone.Value, Verificado: false);
    }
}
