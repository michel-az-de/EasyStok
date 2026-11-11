using EasyStock.Application.UseCases.Atendimento.Avisos;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities.Atendimento;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

/// <summary>Tela Avisos (#2096): quais avisos da operação chegam à dona e ao gerente, e com que folga.</summary>
[SwaggerTag("Operation alerts for the owner and manager")]
[ApiController]
[Route("api/atendimento/avisos")]
[Authorize(Policy = "Admin")]
public class AtendimentoAvisosController(
    ObterAvisosOperacaoUseCase obter,
    SalvarAvisosOperacaoUseCase salvar,
    ListarInscricoesAvisosEquipeUseCase listarEquipe,
    SalvarInscricaoAvisosEquipeUseCase salvarEquipe,
    ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    [SwaggerOperation(Summary = "Get the store's operation alerts (Admin only)",
        Description = "Sem registro, devolve o padrão. O tempo do cliente esperando é o SLA da aba Atendimento.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [HttpGet]
    public async Task<IActionResult> Get() => DataOk(await obter.ExecuteAsync(currentUser.EmpresaId));

    [SwaggerOperation(Summary = "Save the store's operation alerts (Admin only)",
        Description = "Grava o documento inteiro. Tempo 0 desliga o aviso; fora da faixa devolve 400 com a mensagem.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [HttpPut]
    public async Task<IActionResult> Put([FromBody] AvisosOperacaoBody body)
    {
        try
        {
            return DataOk(await salvar.ExecuteAsync(currentUser.EmpresaId, body.ParaDominio()));
        }
        catch (UseCaseValidationException ex)
        {
            return DataBadRequest(ex);
        }
    }

    [SwaggerOperation(Summary = "List who receives the team alerts on WhatsApp (Admin only)",
        Description = "#2211: a equipe ativa da loja e os três avisos da equipe de cada pessoa. Sem telefone confirmado nada chega.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [HttpGet("equipe")]
    public async Task<IActionResult> GetEquipe(CancellationToken ct) =>
        DataOk(await listarEquipe.ExecuteAsync(currentUser.EmpresaId, ct));

    [SwaggerOperation(Summary = "Save which team alerts a person receives on WhatsApp (Admin only)",
        Description = "#2211: grava os três de uma vez. Pessoa fora da equipe ativa da loja devolve 400.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [HttpPut("equipe/{usuarioId:guid}")]
    public async Task<IActionResult> PutEquipe(Guid usuarioId, [FromBody] InscricaoAvisosEquipeBody body, CancellationToken ct)
    {
        try
        {
            return DataOk(await salvarEquipe.ExecuteAsync(
                currentUser.EmpresaId, usuarioId, body.Atencao, body.Pedido, body.Pagamento, ct));
        }
        catch (UseCaseValidationException ex)
        {
            return DataBadRequest(ex);
        }
    }
}

/// <summary>#2211: os três avisos da equipe de uma pessoa.</summary>
public sealed record InscricaoAvisosEquipeBody(bool Atencao, bool Pedido, bool Pagamento);

public sealed record AvisosOperacaoBody(
    bool ClienteSemResposta = true,
    bool PedidoAtrasado = true,
    bool ImpressaoTravada = true,
    bool CaixaEsquecido = true,
    int ProntoSemEntregadorMinutos = AvisosOperacao.ProntoSemEntregadorPadrao,
    int EncomendaProximaHoras = AvisosOperacao.EncomendaProximaPadrao,
    int EncomendaSemConfirmacaoHoras = AvisosOperacao.EncomendaSemConfirmacaoPadrao,
    int EstoqueVencendoDias = AvisosOperacao.EstoqueVencendoPadrao,
    int EstoqueParadoDias = AvisosOperacao.EstoqueParadoPadrao,
    bool ReclamacaoCliente = true,
    bool SuspeitaAtendimento = true,
    bool EquipeAtencao = true,
    bool EquipePedido = true,
    bool EquipePagamento = true)
{
    public AvisosOperacao ParaDominio() => new()
    {
        ClienteSemResposta = ClienteSemResposta,
        PedidoAtrasado = PedidoAtrasado,
        ImpressaoTravada = ImpressaoTravada,
        CaixaEsquecido = CaixaEsquecido,
        ProntoSemEntregadorMinutos = ProntoSemEntregadorMinutos,
        EncomendaProximaHoras = EncomendaProximaHoras,
        EncomendaSemConfirmacaoHoras = EncomendaSemConfirmacaoHoras,
        EstoqueVencendoDias = EstoqueVencendoDias,
        EstoqueParadoDias = EstoqueParadoDias,
        ReclamacaoCliente = ReclamacaoCliente,
        SuspeitaAtendimento = SuspeitaAtendimento,
        EquipeAtencao = EquipeAtencao,
        EquipePedido = EquipePedido,
        EquipePagamento = EquipePagamento,
    };
}
