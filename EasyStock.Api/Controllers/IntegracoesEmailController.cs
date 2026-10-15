using EasyStock.Application.UseCases.Atendimento.Email;
using EasyStock.Application.UseCases.Common;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

/// <summary>
/// Caixa de suporte da loja para o atendimento por e-mail (#1432): o console grava endereço, servidores, usuário e
/// senha (cifrada em <c>credencial_integracao</c>) e testa a conexão. A senha nunca volta: a leitura diz só se ela
/// está definida.
/// </summary>
[SwaggerTag("Support mailbox (IMAP/SMTP) for customer service by e-mail")]
[ApiController]
[Route("api/integracoes/email")]
[Authorize(Policy = "Admin")]
public class IntegracoesEmailController(ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    private const string CodigoCifragem = "CIFRAGEM_NAO_CONFIGURADA";
    private const string MensagemCifragem =
        "A cifragem de credenciais não está configurada neste servidor (Crypto:CurrentKekId e Crypto:Keks).";

    [SwaggerOperation(Summary = "Support mailbox of the tenant, without the password (Admin only)",
        Description = "data is null when the mailbox was never configured.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [HttpGet("caixa")]
    public async Task<IActionResult> GetCaixa([FromServices] ObterCaixaEmailUseCase obter, CancellationToken ct) =>
        DataOk(await obter.ExecuteAsync(currentUser.EmpresaId, ct));

    [SwaggerOperation(Summary = "Save the support mailbox (Admin only)",
        Description = "Empty password keeps the saved one. 503 when credential encryption is not configured.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    [HttpPut("caixa")]
    public async Task<IActionResult> PutCaixa(
        [FromBody] CaixaEmailEntrada? req, [FromServices] SalvarCaixaEmailUseCase salvar, CancellationToken ct)
    {
        if (req is null) return DataBadRequest("Corpo da requisição ausente.");
        try
        {
            return DataOk(await salvar.ExecuteAsync(currentUser.EmpresaId, currentUser.UsuarioId, req, ct));
        }
        catch (UseCaseValidationException ex) { return DataBadRequest(ex.Message); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Crypto", StringComparison.Ordinal) || ex.Message.Contains("KEK", StringComparison.OrdinalIgnoreCase))
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new ApiErrorResponse(new ApiError(CodigoCifragem, MensagemCifragem, null, null)));
        }
    }

    /// <summary>
    /// Entra no IMAP e no SMTP e sai, sem ler nem enviar nada. Com corpo, testa os dados da tela (senha vazia usa
    /// a gravada); sem corpo, a caixa gravada.
    /// </summary>
    [SwaggerOperation(Summary = "Test the support mailbox login on IMAP and SMTP without reading or sending (Admin only)")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [HttpPost("caixa/teste")]
    public async Task<IActionResult> PostTeste(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] CaixaEmailEntrada? req,
        [FromServices] TestarCaixaEmailUseCase testar, CancellationToken ct)
    {
        try
        {
            var r = await testar.ExecuteAsync(currentUser.EmpresaId, req, ct);
            return DataOk(new { ok = r.Ok, imapOk = r.ImapOk, imapErro = r.ImapErro, smtpOk = r.SmtpOk, smtpErro = r.SmtpErro });
        }
        catch (UseCaseValidationException ex) { return DataBadRequest(ex.Message); }
    }
}
