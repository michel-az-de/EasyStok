using System.Security.Cryptography;

namespace EasyStock.Application.UseCases.Storefront.Avaliacao;

/// <summary>
/// Valida o JWT do link WhatsApp, emite cookie HttpOnly e retorna redirect para URL limpa.
///
/// <para>
/// Fluxo: GET /avaliar/abrir?p={pedidoId}&amp;t={jwt}
/// → valida JWT → gera cookie aleatório → armazena hash no <see cref="AvaliacaoCookieStore"/>
/// → retorna <see cref="AbrirPaginaAvaliacaoResult"/> para o controller emitir o cookie e redirecionar.
/// </para>
/// </summary>
public sealed class AbrirPaginaAvaliacaoUseCase(
    AvaliacaoTokenService tokenService,
    AvaliacaoCookieStore cookieStore)
{
    public async Task<AbrirPaginaAvaliacaoResult> ExecuteAsync(AbrirPaginaAvaliacaoInput input)
    {
        tokenService.Validar(input.JwtToken, input.PedidoId);

        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        var cookieValue = Convert.ToBase64String(bytes)
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        await cookieStore.RegistrarAsync(input.PedidoId, cookieValue);

        return new AbrirPaginaAvaliacaoResult(input.PedidoId, cookieValue);
    }
}

public sealed record AbrirPaginaAvaliacaoInput(Guid PedidoId, string JwtToken);

public sealed record AbrirPaginaAvaliacaoResult(Guid PedidoId, string CookieValue);
