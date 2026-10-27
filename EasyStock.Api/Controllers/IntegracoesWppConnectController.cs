using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.UseCases.Atendimento;

namespace EasyStock.Api.Controllers;

[ApiController]
[Route("api/integracoes/whatsapp/wppconnect")]
[Authorize(Policy = "Admin")]
public sealed class IntegracoesWppConnectController(GerenciarWppConnectUseCase gerenciar, ICurrentUserAccessor usuario)
    : EasyStockControllerBase
{
    [HttpGet]
    public Task<IActionResult> Obter(CancellationToken ct) => Executar(() => gerenciar.ObterAsync(usuario.EmpresaId, ct));

    [HttpPost]
    public Task<IActionResult> Conectar(CancellationToken ct) =>
        Executar(() => gerenciar.ConectarAsync(usuario.EmpresaId, usuario.UsuarioId, ct));

    [HttpPost("reconectar")]
    public Task<IActionResult> Reconectar(CancellationToken ct) =>
        Executar(() => gerenciar.ReconectarAsync(usuario.EmpresaId, usuario.UsuarioId, ct));

    [HttpDelete]
    public Task<IActionResult> Desconectar(CancellationToken ct) => Executar(async () =>
    {
        await gerenciar.DesconectarAsync(usuario.EmpresaId, ct);
        return new EstadoWppConnect(true, false, false, "NAO_VINCULADO");
    });

    private async Task<IActionResult> Executar(Func<Task<EstadoWppConnect>> operacao)
    {
        Response.Headers.CacheControl = "no-store"; // o QR autentica um dispositivo
        try { return DataOk(await operacao()); }
        catch (Exception ex) when (ex is WhatsAppCloudException or HttpRequestException or TaskCanceledException)
        {
            return StatusCode(502, new { error = new { code = "WPP_INDISPONIVEL",
                message = "O conector do WhatsApp não confirmou a operação. Confira o celular e tente atualizar a conexão." } });
        }
    }
}
