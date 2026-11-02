namespace EasyStock.Application.Ports.Output.Auth;

/// <summary>Protege somente o sucessor recuperável da última renovação nativa, vinculado à sua família.</summary>
public interface IProtetorRefreshNativo
{
    string Proteger(Guid sessaoId, string refreshToken);
    string Desproteger(Guid sessaoId, string protegido);
}
