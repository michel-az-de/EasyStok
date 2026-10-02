using EasyStock.Application.Services.Auth;

namespace EasyStock.Application.UseCases.AtribuirPerfilUsuario
{
    public sealed record AtribuirPerfilUsuarioCommand(Guid UsuarioId, Guid EmpresaId, Guid PerfilId, Guid? LojaId);

    public class AtribuirPerfilUsuarioUseCase(
        IUsuarioRepository usuarioRepository,
        IUsuarioPerfilRepository usuarioPerfilRepository,
        IPerfilRepository perfilRepository,
        RevogadorSessoes revogadorSessoes,
        IUnitOfWork unitOfWork,
        ILogger<AtribuirPerfilUsuarioUseCase> logger)
    {
        public async Task ExecuteAsync(AtribuirPerfilUsuarioCommand command)
        {
            UseCaseGuards.EnsureEmpresaId(command.EmpresaId);

            logger.LogInformation("Atribuindo perfil {PerfilId} ao usuario {UsuarioId}", command.PerfilId, command.UsuarioId);

            var usuario = await usuarioRepository.GetByIdAsync(command.UsuarioId)
                ?? throw new UseCaseValidationException("Usuario nao encontrado.");

            // Perfil global SuperAdmin ou de outra empresa nao pode ser atribuido por um Admin de tenant
            // (o login promove a SuperAdmin quem tiver QUALQUER perfil com esse nivel).
            var perfil = await perfilRepository.GetByIdAsync(command.PerfilId)
                ?? throw new UseCaseValidationException("Perfil invalido.");
            if (perfil.Nivel == NivelAcesso.SuperAdmin || (perfil.EmpresaId is not null && perfil.EmpresaId != command.EmpresaId))
                throw new UseCaseValidationException("Perfil invalido.");

            var perfilExistente = await usuarioPerfilRepository.GetByUsuarioEmpresaEPerfilAsync(command.UsuarioId, command.EmpresaId, command.PerfilId);

            if (perfilExistente is not null)
            {
                perfilExistente.LojaId = command.LojaId;
                perfilExistente.AtribuidoEm = DateTime.UtcNow;
                await usuarioPerfilRepository.UpdateAsync(perfilExistente);
            }
            else
            {
                var usuarioPerfil = new UsuarioPerfil
                {
                    Id = Guid.NewGuid(),
                    UsuarioId = command.UsuarioId,
                    EmpresaId = command.EmpresaId,
                    PerfilId = command.PerfilId,
                    LojaId = command.LojaId,
                    AtribuidoEm = DateTime.UtcNow
                };

                await usuarioPerfilRepository.AddAsync(usuarioPerfil);
            }

            // #1352: nível e permissões viajam no JWT, então quem tinha token com o perfil antigo cai. O corte é do
            // usuário todo e esta rota só confere a empresa de quem chama: sem vínculo ativo com a empresa da
            // atribuição, o Admin de outra empresa deslogaria quem não é dele.
            if (usuario.Empresas.Any(e => e.EmpresaId == command.EmpresaId && e.Ativo))
                await revogadorSessoes.RevogarAsync(usuario);

            await unitOfWork.CommitAsync();
        }
    }
}
