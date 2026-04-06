using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;

namespace EasyStock.Application.UseCases.ListarUsuarios
{
    public sealed record UsuarioResult(
        Guid UsuarioId,
        string Nome,
        string Email,
        bool Ativo,
        DateTime? UltimoAcessoEm,
        DateTime CriadoEm);

    public sealed record ListarUsuariosQuery(Guid EmpresaId, int Page = 1, int PageSize = 20);

    public class ListarUsuariosUseCase(IUsuarioRepository usuarioRepository)
    {
        public async Task<(IEnumerable<UsuarioResult> Usuarios, int Total)> ExecuteAsync(ListarUsuariosQuery query)
        {
            var (usuarios, total) = await usuarioRepository.GetByEmpresaAsync(query.EmpresaId, query.Page, query.PageSize);
            return (usuarios.Select(ToResult), total);
        }

        private static UsuarioResult ToResult(Usuario u) =>
            new(u.Id, u.Nome, u.Email, u.Ativo, u.UltimoAcessoEm, u.CriadoEm);
    }
}
