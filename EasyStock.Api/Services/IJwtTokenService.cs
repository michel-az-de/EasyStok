using EasyStock.Application.UseCases.AutenticarUsuario;

namespace EasyStock.Api.Services;

public interface IJwtTokenService : EasyStock.Application.Ports.Output.IJwtTokenService
{
    string GerarRefreshToken();
}
