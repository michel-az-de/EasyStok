using EasyStock.Application.UseCases.Atendimento.Entregas;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Enums.Atendimento;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

/// <summary>
/// Entregadores (S44, ADR-0051): cadastro manual de motoboy, plataforma ou próprio. A integração com
/// 99, Lalamove e iFood Entregas fica fora e vai preencher os mesmos campos.
/// </summary>
[SwaggerTag("Couriers")]
[ApiController]
[Route("api/atendimento/entregadores")]
[Authorize(Policy = "Operador")]
public class AtendimentoEntregadoresController(
    CriarEntregadorUseCase criarUseCase,
    AtualizarEntregadorUseCase atualizarUseCase,
    AlterarAtivoEntregadorUseCase ativoUseCase,
    ListarEntregadoresUseCase listarUseCase,
    ObterEntregadorUseCase obterUseCase,
    ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    [SwaggerOperation(Summary = "List couriers", Description = "Ativos por padrão; incluirInativos=true traz todos.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [HttpGet]
    public async Task<IActionResult> Listar([FromQuery] bool incluirInativos, CancellationToken ct)
        => DataOk(await listarUseCase.ExecuteAsync(currentUser.EmpresaId, incluirInativos, ct));

    [SwaggerOperation(Summary = "Get a courier")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpGet("{id:guid}")]
    public Task<IActionResult> Obter(Guid id, CancellationToken ct)
        => Executar(async () => DataOk(await obterUseCase.ExecuteAsync(currentUser.EmpresaId, id, ct)));

    [SwaggerOperation(Summary = "Create a courier")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [HttpPost]
    public Task<IActionResult> Criar([FromBody] EntregadorBody body, CancellationToken ct)
        => Executar(async () => DataOk(await criarUseCase.ExecuteAsync(new CriarEntregadorCommand(
            currentUser.EmpresaId, body.Nome, body.Tipo, body.Empresa, body.Telefone, body.Veiculo, body.Placa), ct)));

    [SwaggerOperation(Summary = "Update a courier", Description = "Não altera o retrato gravado nas paradas já despachadas.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpPut("{id:guid}")]
    public Task<IActionResult> Atualizar(Guid id, [FromBody] EntregadorBody body, CancellationToken ct)
        => Executar(async () => DataOk(await atualizarUseCase.ExecuteAsync(new AtualizarEntregadorCommand(
            currentUser.EmpresaId, id, body.Nome, body.Tipo, body.Empresa, body.Telefone, body.Veiculo, body.Placa), ct)));

    [SwaggerOperation(Summary = "Deactivate a courier", Description = "Inativo não entra em viagem. O histórico fica.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpDelete("{id:guid}")]
    public Task<IActionResult> Desativar(Guid id, CancellationToken ct)
        => Executar(async () => DataOk(await ativoUseCase.ExecuteAsync(currentUser.EmpresaId, id, ativo: false, ct)));

    [SwaggerOperation(Summary = "Reactivate a courier")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpPost("{id:guid}/reativar")]
    public Task<IActionResult> Reativar(Guid id, CancellationToken ct)
        => Executar(async () => DataOk(await ativoUseCase.ExecuteAsync(currentUser.EmpresaId, id, ativo: true, ct)));

    private async Task<IActionResult> Executar(Func<Task<IActionResult>> acao)
    {
        try { return await acao(); }
        catch (UseCaseValidationException ex) { return DataBadRequest(ex.Message); }
        catch (EntregadorNaoEncontradoException ex) { return DataNotFound(ex.Message); }
    }
}

/// <summary>
/// Viagens de entrega (S44): montar (incluir, retirar e reordenar paradas), sair (RN-32: sem
/// entregador é recusado), marcar parada entregue e desfazer. O despacho devolve, por parada, o número
/// do pedido e o retrato do entregador, com o link de rota no Google Maps. Também os chamados de entregador.
/// </summary>
[SwaggerTag("Delivery trips")]
[ApiController]
[Route("api/atendimento")]
[Authorize(Policy = "Operador")]
public class AtendimentoViagensController(
    ListarViagensUseCase listarUseCase,
    ObterViagemUseCase obterUseCase,
    CriarViagemUseCase criarUseCase,
    DefinirEntregadorViagemUseCase entregadorUseCase,
    IncluirParadaViagemUseCase incluirUseCase,
    RetirarParadaViagemUseCase retirarUseCase,
    ReordenarParadaViagemUseCase reordenarUseCase,
    SairParaEntregaUseCase sairUseCase,
    MarcarParadaEntregueUseCase entregueUseCase,
    DesfazerViagemUseCase desfazerUseCase,
    AbrirChamadoEntregadorUseCase abrirChamadoUseCase,
    ResolverChamadoEntregadorUseCase resolverChamadoUseCase,
    ListarChamadosEntregadorUseCase listarChamadosUseCase,
    ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    private Guid EmpresaId => currentUser.EmpresaId;

    [SwaggerOperation(Summary = "List delivery trips (up to 50, newest first)")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [HttpGet("viagens")]
    public async Task<IActionResult> Listar([FromQuery] SituacaoViagem? situacao, CancellationToken ct)
        => DataOk(await listarUseCase.ExecuteAsync(EmpresaId, situacao, ct));

    [SwaggerOperation(Summary = "Get a trip (dispatch view with route link)")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpGet("viagens/{id:guid}")]
    public Task<IActionResult> Obter(Guid id, CancellationToken ct)
        => Executar(async () => DataOk(await obterUseCase.ExecuteAsync(EmpresaId, id, ct)));

    [SwaggerOperation(Summary = "Create a trip")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [HttpPost("viagens")]
    public Task<IActionResult> Criar([FromBody] CriarViagemBody? body, CancellationToken ct)
        => Executar(async () => DataOk(await criarUseCase.ExecuteAsync(EmpresaId, body?.EntregadorId, ct)));

    [SwaggerOperation(Summary = "Set or clear the trip courier")]
    [HttpPut("viagens/{id:guid}/entregador")]
    public Task<IActionResult> DefinirEntregador(Guid id, [FromBody] DefinirEntregadorBody body, CancellationToken ct)
        => ExecutarEObter(id, () => entregadorUseCase.ExecuteAsync(EmpresaId, id, body.EntregadorId, ct), ct);

    [SwaggerOperation(Summary = "Add an order as the last stop", Description = "Cliente bloqueado não entra (RN-14).")]
    [HttpPost("viagens/{id:guid}/paradas")]
    public Task<IActionResult> IncluirParada(Guid id, [FromBody] ParadaBody body, CancellationToken ct)
        => ExecutarEObter(id, () => incluirUseCase.ExecuteAsync(EmpresaId, id, body.PedidoId, ct), ct);

    [SwaggerOperation(Summary = "Remove a stop")]
    [HttpDelete("viagens/{id:guid}/paradas/{pedidoId:guid}")]
    public Task<IActionResult> RetirarParada(Guid id, Guid pedidoId, CancellationToken ct)
        => ExecutarEObter(id, () => retirarUseCase.ExecuteAsync(EmpresaId, id, pedidoId, ct), ct);

    [SwaggerOperation(Summary = "Move a stop to a new position (1-based)")]
    [HttpPut("viagens/{id:guid}/paradas/{pedidoId:guid}/ordem")]
    public Task<IActionResult> Reordenar(Guid id, Guid pedidoId, [FromBody] ReordenarBody body, CancellationToken ct)
        => ExecutarEObter(id, () => reordenarUseCase.ExecuteAsync(EmpresaId, id, pedidoId, body.Ordem, ct), ct);

    [SwaggerOperation(Summary = "Dispatch the trip",
        Description = "Todos os pedidos vão para saiu_para_entrega e cada cliente é avisado. Sem entregador: 400 (RN-32).")]
    [HttpPost("viagens/{id:guid}/sair")]
    public Task<IActionResult> Sair(Guid id, CancellationToken ct)
        => ExecutarEObter(id, () => sairUseCase.ExecuteAsync(EmpresaId, id, ct), ct);

    [SwaggerOperation(Summary = "Mark a stop delivered", Description = "O pedido vai para entregue; a última parada conclui a viagem.")]
    [HttpPost("viagens/{id:guid}/paradas/{pedidoId:guid}/entregue")]
    public Task<IActionResult> MarcarEntregue(Guid id, Guid pedidoId, CancellationToken ct)
        => ExecutarEObter(id, () => entregueUseCase.ExecuteAsync(EmpresaId, id, pedidoId, ct), ct);

    [SwaggerOperation(Summary = "Undo a trip before dispatch")]
    [HttpPost("viagens/{id:guid}/desfazer")]
    public Task<IActionResult> Desfazer(Guid id, CancellationToken ct)
        => ExecutarEObter(id, () => desfazerUseCase.ExecuteAsync(EmpresaId, id, ct), ct);

    [SwaggerOperation(Summary = "List courier calls", Description = "Abertos por padrão; todos=true traz os resolvidos.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [HttpGet("chamados-entregador")]
    public async Task<IActionResult> ListarChamados([FromQuery] bool todos, CancellationToken ct)
        => DataOk(await listarChamadosUseCase.ExecuteAsync(EmpresaId, apenasAbertos: !todos, ct));

    [SwaggerOperation(Summary = "Open a courier call (free text)")]
    [HttpPost("chamados-entregador")]
    public Task<IActionResult> AbrirChamado([FromBody] ChamadoBody body, CancellationToken ct)
        => Executar(async () => DataOk(await abrirChamadoUseCase.ExecuteAsync(EmpresaId, body.Texto, body.ViagemId, ct)));

    [SwaggerOperation(Summary = "Mark a courier call attended")]
    [HttpPost("chamados-entregador/{id:guid}/atender")]
    public Task<IActionResult> AtenderChamado(Guid id, CancellationToken ct)
        => Executar(async () => DataOk(await resolverChamadoUseCase.ExecuteAsync(EmpresaId, id, atendido: true, ct)));

    [SwaggerOperation(Summary = "Cancel a courier call")]
    [HttpPost("chamados-entregador/{id:guid}/cancelar")]
    public Task<IActionResult> CancelarChamado(Guid id, CancellationToken ct)
        => Executar(async () => DataOk(await resolverChamadoUseCase.ExecuteAsync(EmpresaId, id, atendido: false, ct)));

    private Task<IActionResult> ExecutarEObter(Guid id, Func<Task> acao, CancellationToken ct)
        => Executar(async () =>
        {
            await acao();
            return DataOk(await obterUseCase.ExecuteAsync(EmpresaId, id, ct));
        });

    private async Task<IActionResult> Executar(Func<Task<IActionResult>> acao)
    {
        try { return await acao(); }
        catch (UseCaseValidationException ex) { return DataBadRequest(ex.Message); }
        catch (ViagemNaoEncontradaException ex) { return DataNotFound(ex.Message); }
        catch (EntregadorNaoEncontradoException ex) { return DataNotFound(ex.Message); }
        catch (PedidoNaoEncontradoParaViagemException ex) { return DataNotFound(ex.Message); }
        catch (ChamadoEntregadorNaoEncontradoException ex) { return DataNotFound(ex.Message); }
    }
}

/// <summary>Relatório de entregas por bairro (S44): só a dona e o administrador.</summary>
[SwaggerTag("Delivery reports")]
[ApiController]
[Route("api/atendimento/relatorios")]
[Authorize(Policy = "Admin")]
public class AtendimentoRelatoriosEntregaController(
    EntregasPorBairroUseCase entregasPorBairroUseCase,
    ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    [SwaggerOperation(Summary = "Delivered orders and value by neighborhood",
        Description = "Pedidos entregues em [de, ate), do maior valor para o menor. Bairro do cadastro do cliente.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [HttpGet("entregas-por-bairro")]
    public async Task<IActionResult> EntregasPorBairro([FromQuery] DateTime de, [FromQuery] DateTime ate, CancellationToken ct)
    {
        try
        {
            return DataOk(await entregasPorBairroUseCase.ExecuteAsync(currentUser.EmpresaId, de, ate, ct));
        }
        catch (UseCaseValidationException ex)
        {
            return DataBadRequest(ex.Message);
        }
    }
}

public sealed record EntregadorBody(
    string Nome, TipoEntregador Tipo, EmpresaEntregador Empresa, string? Telefone, string? Veiculo, string? Placa);

public sealed record CriarViagemBody(Guid? EntregadorId);

public sealed record DefinirEntregadorBody(Guid? EntregadorId);

public sealed record ParadaBody(Guid PedidoId);

public sealed record ReordenarBody(int Ordem);

public sealed record ChamadoBody(string Texto, Guid? ViagemId);
