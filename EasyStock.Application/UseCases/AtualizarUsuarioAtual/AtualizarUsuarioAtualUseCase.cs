using EasyStock.Application.Services.Auth;

namespace EasyStock.Application.UseCases.AtualizarUsuarioAtual;

public sealed class AtualizarUsuarioAtualUseCase(
    IUsuarioRepository usuarioRepository,
    ICurrentUserAccessor currentUserAccessor,
    IUnitOfWork unitOfWork,
    TrocaDeContatoService trocaDeContato,
    ILogger<AtualizarUsuarioAtualUseCase> logger) : IUseCase<AtualizarUsuarioAtualCommand, AtualizarUsuarioAtualResult>
{
    public async Task<AtualizarUsuarioAtualResult> ExecuteAsync(AtualizarUsuarioAtualCommand command)
    {
        logger.LogInformation("Atualizando dados do usuario atual");

        var usuarioId = currentUserAccessor.UsuarioId;
        if (usuarioId == Guid.Empty)
        {
            throw new UsuarioNaoAutorizadoException("Usuario nao autenticado.");
        }

        var usuario = await usuarioRepository.GetByIdAsync(usuarioId);
        if (usuario == null)
        {
            throw new RegraDeDominioVioladaException("Usuario nao encontrado.");
        }

        // N4: trocar o e-mail é o caminho mais curto para tomar a conta de alguém. Exige a senha atual (a errada é 403 e
        // conta como falha de login) e vira duas etapas: o Email só muda no clique do link enviado ao endereço novo.
        if (!string.IsNullOrWhiteSpace(command.Email) &&
            !string.Equals(usuario.Email, command.Email.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            await trocaDeContato.ExigirSenhaAtualAsync(usuario, command.SenhaAtual);
            var empresaId = await trocaDeContato.ResolverEmpresaDoEventoAsync();
            await trocaDeContato.SolicitarTrocaDeEmailAsync(usuario, command.Email, command.BaseUrl, empresaId);
        }

        if (!string.IsNullOrWhiteSpace(command.Nome))
            usuario.Nome = command.Nome;

        if (!string.IsNullOrWhiteSpace(command.TemaPreferido))
            usuario.TemaPreferido = string.Equals(command.TemaPreferido, "dark", StringComparison.OrdinalIgnoreCase) ? "dark" : "light";

        usuario.AlteradoEm = DateTime.UtcNow;

        await usuarioRepository.UpdateAsync(usuario);
        await unitOfWork.CommitAsync();

        logger.LogInformation("Usuario {UsuarioId} atualizado", usuario.Id);
        return new AtualizarUsuarioAtualResult(
            usuario.Id, usuario.Nome, usuario.Email, usuario.TemaPreferido, usuario.EmailPendente);
    }
}
