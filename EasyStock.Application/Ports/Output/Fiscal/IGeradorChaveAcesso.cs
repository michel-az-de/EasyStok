namespace EasyStock.Application.Ports.Output.Fiscal;

/// <summary>
/// Gera o código numérico aleatório de 8 dígitos (cNF) usado na chave
/// de acesso. Implementação usa <see cref="System.Security.Cryptography.RandomNumberGenerator"/>
/// para garantir entropia adequada. Stub testavel em testes via NSubstitute.
/// </summary>
public interface IGeradorChaveAcesso
{
    string GerarCodigoNumerico8();
}
