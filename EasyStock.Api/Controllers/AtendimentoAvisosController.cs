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
}

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
    bool SuspeitaAtendimento = true)
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
    };
}
