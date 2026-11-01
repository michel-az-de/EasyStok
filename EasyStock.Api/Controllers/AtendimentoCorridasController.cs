using EasyStock.Application.Ports.Output.Integration.Logistica;
using EasyStock.Application.UseCases.Atendimento.Entregas;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Enums.Atendimento;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

/// <summary>
/// Corrida externa de uma viagem (I9.1, ADR-0061 D5): a 99, a Lalamove ou a Uber pedida pelo app, registrada
/// à mão com o custo. A viagem é sempre a da empresa do token.
/// </summary>
[SwaggerTag("External delivery rides and their cost")]
[ApiController]
[Route("api/atendimento/viagens/{viagemId:guid}/corridas")]
[Authorize(Policy = "Operador")]
public class AtendimentoCorridasController(
    RegistrarCorridaExternaUseCase registrarUseCase,
    CorrigirCorridaExternaUseCase corrigirUseCase,
    ListarCorridasDaViagemUseCase listarUseCase,
    CotarCorridaUseCase cotarUseCase,
    ChamarCorridaUseCase chamarUseCase,
    SincronizarCorridaUseCase sincronizarUseCase,
    CancelarCorridaUseCase cancelarUseCase,
    AdicionarGorjetaCorridaUseCase gorjetaUseCase,
    ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    [SwaggerOperation(Summary = "List the external rides of a trip")]
    [HttpGet]
    public Task<IActionResult> Listar(Guid viagemId, CancellationToken ct) =>
        Executar(async () => DataOk(await listarUseCase.ExecuteAsync(currentUser.EmpresaId, viagemId, ct)));

    [SwaggerOperation(Summary = "Register an external ride with its cost",
        Description = "Custo em centavos, de zero a R$ 5.000,00. Viagem desfeita e entregador próprio são recusados.")]
    [HttpPost]
    public Task<IActionResult> Registrar(Guid viagemId, [FromBody] CorridaExternaBody body, CancellationToken ct) =>
        Executar(async () => DataOk(await registrarUseCase.ExecuteAsync(new RegistrarCorridaExternaInput(
            currentUser.EmpresaId, currentUser.UsuarioId, viagemId, body.Provedor, body.CustoCentavos, body.Motorista), ct)));

    [SwaggerOperation(Summary = "Correct the cost or driver of an external ride", Description = "Guarda quem corrigiu e quando.")]
    [HttpPut("{corridaId:guid}")]
    public Task<IActionResult> Corrigir(Guid viagemId, Guid corridaId, [FromBody] CorrecaoCorridaExternaBody body, CancellationToken ct) =>
        Executar(async () => DataOk(await corrigirUseCase.ExecuteAsync(new CorrigirCorridaExternaInput(
            currentUser.EmpresaId, currentUser.UsuarioId, viagemId, corridaId, body.CustoCentavos, body.Motorista), ct)));

    [SwaggerOperation(Summary = "Quote a ride on the delivery provider API",
        Description = "#2160: uma parada por corrida (R7). Coleta na cozinha da loja, entrega no endereço do pedido. A cotação vale 5 min.")]
    [HttpPost("cotacao")]
    public Task<IActionResult> Cotar(Guid viagemId, [FromBody] CotarCorridaBody body, CancellationToken ct) =>
        Executar(async () => DataOk(await cotarUseCase.ExecuteAsync(
            new CotarCorridaInput(currentUser.EmpresaId, viagemId, body.Veiculo, body.AgendarPara), ct)));

    [SwaggerOperation(Summary = "Call the quoted ride on the delivery provider API",
        Description = "#2160: só com o pedido pronto (R8). O preço vem da cotação relida no provedor. Uma corrida em curso por viagem.")]
    [HttpPost("chamar")]
    public Task<IActionResult> Chamar(Guid viagemId, [FromBody] ChamarCorridaBody body, CancellationToken ct) =>
        Executar(async () => DataOk(await chamarUseCase.ExecuteAsync(new ChamarCorridaInput(
            currentUser.EmpresaId, currentUser.UsuarioId, viagemId, body.CotacaoId, body.Veiculo, body.Observacao), ct)));

    [SwaggerOperation(Summary = "Refresh a ride from the provider", Description = "Reconsulta o provedor e aplica o status na viagem e no pedido.")]
    [HttpGet("{corridaId:guid}")]
    public Task<IActionResult> Atualizar(Guid viagemId, Guid corridaId, CancellationToken ct) =>
        Executar(async () =>
        {
            var corrida = await sincronizarUseCase.ExecuteAsync(currentUser.EmpresaId, corridaId, ct);
            return corrida.ViagemId == viagemId ? DataOk(corrida) : throw new CorridaExternaNaoEncontradaException(corridaId);
        });

    [SwaggerOperation(Summary = "Cancel a ride on the provider", Description = "Só procurando motorista ou até 5 min depois do match.")]
    [HttpPost("{corridaId:guid}/cancelar")]
    public Task<IActionResult> Cancelar(Guid viagemId, Guid corridaId, CancellationToken ct) =>
        Executar(async () => DataOk(await cancelarUseCase.ExecuteAsync(currentUser.EmpresaId, viagemId, corridaId, ct)));

    [SwaggerOperation(Summary = "Add a priority fee to find a driver faster", Description = "Só enquanto procura motorista. Centavos, até R$ 100,00.")]
    [HttpPost("{corridaId:guid}/gorjeta")]
    public Task<IActionResult> Gorjeta(Guid viagemId, Guid corridaId, [FromBody] GorjetaCorridaBody body, CancellationToken ct) =>
        Executar(async () => DataOk(await gorjetaUseCase.ExecuteAsync(currentUser.EmpresaId, viagemId, corridaId, body.Centavos, ct)));

    private async Task<IActionResult> Executar(Func<Task<IActionResult>> acao)
    {
        try { return await acao(); }
        catch (UseCaseValidationException ex) { return DataBadRequest(ex); }
        catch (LogisticaNaoConectadaException ex) { return DataConflict(ex.Message); }
        catch (ViagemNaoEncontradaException ex) { return DataNotFound(ex.Message); }
        catch (CorridaExternaNaoEncontradaException ex) { return DataNotFound(ex.Message); }
    }
}

public sealed record CorridaExternaBody(EmpresaEntregador Provedor, int CustoCentavos, string? Motorista);

public sealed record CorrecaoCorridaExternaBody(int CustoCentavos, string? Motorista);

public sealed record CotarCorridaBody(VeiculoCorrida Veiculo = VeiculoCorrida.Moto, DateTimeOffset? AgendarPara = null);

public sealed record ChamarCorridaBody(string CotacaoId, VeiculoCorrida Veiculo = VeiculoCorrida.Moto, string? Observacao = null);

public sealed record GorjetaCorridaBody(int Centavos);

/// <summary>Custo das corridas contra o frete cobrado (I9.1): só a dona e o administrador.</summary>
[SwaggerTag("Delivery reports")]
[ApiController]
[Route("api/atendimento/relatorios")]
[Authorize(Policy = "Admin")]
public class AtendimentoRelatorioCustoDeEntregaController(
    CustoDeEntregaUseCase custoDeEntregaUseCase,
    ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    [SwaggerOperation(Summary = "Ride cost against delivery fee charged, per trip",
        Description = "Viagens criadas em [de, ate), no máximo 93 dias. Viagem desfeita só entra se tiver corrida registrada. Saldo negativo é entrega que custou mais do que rendeu.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [HttpGet("custo-de-entrega")]
    public async Task<IActionResult> CustoDeEntrega([FromQuery] DateTime de, [FromQuery] DateTime ate, CancellationToken ct)
    {
        try
        {
            return DataOk(await custoDeEntregaUseCase.ExecuteAsync(currentUser.EmpresaId, de, ate, ct));
        }
        catch (UseCaseValidationException ex)
        {
            return DataBadRequest(ex);
        }
    }
}