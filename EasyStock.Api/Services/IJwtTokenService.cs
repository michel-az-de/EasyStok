namespace EasyStock.Api.Services;

public interface IJwtTokenService : EasyStock.Application.Ports.Output.IJwtTokenService
{
    string GerarRefreshToken();
}
