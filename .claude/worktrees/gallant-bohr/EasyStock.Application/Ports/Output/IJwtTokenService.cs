using EasyStock.Application.UseCases.AutenticarUsuario;

namespace EasyStock.Application.Ports.Output;

public interface IJwtTokenService
{
    string GerarToken(AutenticarUsuarioResult resultado);
    int ExpiresInSeconds { get; }
}
