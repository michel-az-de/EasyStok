using EasyStock.Application.UseCases.AutenticarUsuario;
namespace EasyStock.Api.Services;
public interface IJwtTokenService
{
    string GerarToken(AutenticarUsuarioResult resultado);
    int ExpiresInSeconds { get; }
}
