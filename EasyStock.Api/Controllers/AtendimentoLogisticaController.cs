using EasyStock.Api.Data.DemoStore;
using EasyStock.Application.UseCases.Atendimento.Entregas;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.Ports.Output.Integrations;

namespace EasyStock.Api.Controllers;

[ApiController]
[Route("api/atendimento/logistica")]
[Authorize(Policy = "Operador")]
public sealed class AtendimentoLogisticaController(
    LogisticaUseCases logistica, LogisticaDemoSeeder demonstracao,
    ICurrentUserAccessor usuario, RastreioLogisticaLink rastreio) : EasyStockControllerBase
{
    [HttpGet("contexto")]
    public Task<IActionResult> Contexto([FromQuery] DateOnly? dia, [FromQuery] Guid? pedidoId, CancellationToken ct)
        => Executar(async () =>
        {
            var contexto = await logistica.ContextoAsync(usuario.EmpresaId, dia, pedidoId, ct);
            return DataOk(contexto with { Despachos = contexto.Despachos.Select(ComRastreio).ToArray() });
        });

    [HttpPost("cotacoes")]
    public Task<IActionResult> Cotar([FromBody] CotarLogisticaInput input, CancellationToken ct)
        => Executar(async () => DataOk(await logistica.CotarAsync(usuario.EmpresaId, input, ct)));

    [HttpPost("despachos")]
    public Task<IActionResult> Contratar([FromBody] ContratarLogisticaInput input, CancellationToken ct)
        => Executar(async () => DataOk(ComRastreio(await logistica.ContratarAsync(usuario.EmpresaId, usuario.UsuarioId, input, ct))));

    [HttpPost("automatico")]
    public Task<IActionResult> Automatico([FromBody] LogisticaAutomaticaBody input, CancellationToken ct)
        => Executar(async () => DataOk(ComRastreio(await logistica.ContratarAutomaticoAsync(usuario.EmpresaId,
            usuario.UsuarioId, new CotarLogisticaInput(input.PedidoIds, input.Agrupar), input.ChaveIdempotencia, ct))));

    [HttpPost("despachos/{id:guid}/pagamento")]
    [Authorize(Policy = "Gerente")]
    public Task<IActionResult> Pagar(Guid id, [FromBody] PagamentoLogisticaInput input, CancellationToken ct)
        => Executar(async () => DataOk(ComRastreio(await logistica.ConfirmarPagamentoAsync(usuario.EmpresaId, usuario.UsuarioId, id, input, ct))));

    [HttpPost("despachos/{id:guid}/evento")]
    public Task<IActionResult> Evento(Guid id, [FromBody] EventoLogisticaInput input, CancellationToken ct)
        => Executar(async () => DataOk(ComRastreio(await logistica.RegistrarEventoAsync(usuario.EmpresaId, id, input, ct))));

    [HttpPut("politica")]
    [Authorize(Policy = "Admin")]
    public Task<IActionResult> Politica([FromBody] PoliticaLogisticaInput input, CancellationToken ct)
        => Executar(async () => DataOk(await logistica.ConfigurarPoliticaAsync(usuario.EmpresaId, input, ct)));

    [HttpPost("demonstracao")]
    [Authorize(Policy = "Admin")]
    public Task<IActionResult> PrepararDemonstracao([FromBody] LogisticaDemonstracaoBody input, CancellationToken ct)
        => Executar(async () => DataOk(await demonstracao.CarregarAsync(usuario.EmpresaId, input.Dia, ct)));

    private DespachoLogisticaResult ComRastreio(DespachoLogisticaResult despacho) => despacho.Simulado
        ? despacho with { RastreioUrl = rastreio.Criar(despacho.Id) } : despacho;

    private async Task<IActionResult> Executar(Func<Task<IActionResult>> acao)
    {
        try { return await acao(); }
        catch (UseCaseValidationException ex) { return DataBadRequest(ex); }
        catch (RegraDeDominioVioladaException ex) { return DataBadRequest(ex.Message); }
        catch (FreteProviderException ex) { return DataBadRequest(ex.Message); }
        catch (HttpRequestException) { return StatusCode(502, new { message = "Não foi possível confirmar o serviço de entrega. Atualize antes de tentar novamente." }); }
    }
}

public sealed record LogisticaAutomaticaBody(IReadOnlyList<Guid> PedidoIds, bool Agrupar, string ChaveIdempotencia);
public sealed record LogisticaDemonstracaoBody(DateOnly Dia);
