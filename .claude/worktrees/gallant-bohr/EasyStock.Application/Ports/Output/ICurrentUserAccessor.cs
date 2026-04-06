using EasyStock.Domain.Enums;

namespace EasyStock.Application.Ports.Output
{
    public interface ICurrentUserAccessor
    {
        Guid EmpresaId { get; }
        bool IsAuthenticated { get; }
        Guid UsuarioId { get; }
        NivelAcesso Nivel { get; }
        bool TemPermissao(Permissao permissao);
    }
}
