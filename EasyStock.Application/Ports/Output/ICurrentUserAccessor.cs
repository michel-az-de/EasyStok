namespace EasyStock.Application.Ports.Output
{
    public interface ICurrentUserAccessor
    {
        Guid EmpresaId { get; }
        bool IsAuthenticated { get; }
        Guid UsuarioId { get; }
        NivelAcesso Nivel { get; }
        bool TemPermissao(Permissao permissao);

        // Contexto de auditoria — null fora de uma request HTTP (jobs, seeds, testes).
        string? Ip => null;
        string? UserAgent => null;
        string? DispositivoId => null;
    }
}
