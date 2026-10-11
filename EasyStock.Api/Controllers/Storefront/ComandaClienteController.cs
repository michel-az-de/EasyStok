using System.Text.Json;
using EasyStock.Api.Services.Operacao;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Comanda;
using EasyStock.Application.UseCases.Common;
using Microsoft.AspNetCore.RateLimiting;

namespace EasyStock.Api.Controllers.Storefront;

[ApiController]
[Route("api/storefront/cardapio-conversa/{token}")]
[AllowAnonymous]
[IgnoreAntiforgeryToken]
public sealed class ComandaClienteController(ComandaClienteUseCase comanda, ListarCardapioComandaUseCase cardapio,
    ListarJanelasAtendimentoUseCase janelas,
    OperacaoEventBroker broker, TimeProvider relogio, IServiceScopeFactory escopos) : ControllerBase
{
    [HttpGet("comanda")]
    [EnableRateLimiting("disponibilidade")]
    public Task<IActionResult> Obter(string token, CancellationToken ct) => Tratar(async () => await comanda.ObterAsync(token, ct));

    [HttpGet("cardapio")]
    [EnableRateLimiting("disponibilidade")]
    public Task<IActionResult> Cardapio(string token, CancellationToken ct) => Tratar(async () =>
        await cardapio.ExecuteAsync((await comanda.AcessoAsync(token, ct)).EmpresaId, ct));

    [HttpGet("janelas")]
    [EnableRateLimiting("disponibilidade")]
    public Task<IActionResult> Janelas(string token, [FromQuery] Guid[]? itens, CancellationToken ct) => Tratar(async () =>
        await janelas.ExecuteAsync(new((await comanda.AcessoAsync(token, ct)).EmpresaId, null, null, itens ?? []), ct));

    [HttpPut("rascunho")]
    [EnableRateLimiting("comanda-rascunho")]
    public Task<IActionResult> Salvar(string token, [FromBody] SalvarComandaClienteBody body, CancellationToken ct) =>
        Tratar(async () => await comanda.SalvarAsync(token, body.Revisao, body.Rascunho, ct));

    [HttpPost("confirmar")]
    [EnableRateLimiting("public-post")]
    public Task<IActionResult> Enviar(string token, [FromBody] EnviarComandaClienteBody body, CancellationToken ct) => Tratar(async () =>
    {
        var resultado = await comanda.EnviarAsync(token, body.Revisao, ct);
        if (resultado.Situacao == "alteracao_aguardando_confirmacao")
        {
            var estado = await comanda.ObterAsync(token, ct);
            // Só acréscimos preservando pratos e observações passam direto. Trocas têm conferência humana.
            if (estado.Pedido is { } pedido && resultado.Total >= pedido.Total
                && pedido.Itens.GroupBy(a => (a.CardapioItemId, a.VariacaoId)).All(g => estado.Selecao.Any(n => n.CardapioItemId == g.Key.CardapioItemId
                    && n.VariacaoId == g.Key.VariacaoId && n.Qtd >= g.Sum(a => a.Quantidade) && g.All(a => (n.Observacao ?? "") == (a.Observacao ?? "")))))
            {
                await using var escopo = escopos.CreateAsyncScope();
                var acesso = await escopo.ServiceProvider.GetRequiredService<ComandaClienteUseCase>().AcessoAsync(token, ct);
                resultado = await escopo.ServiceProvider.GetRequiredService<ConfirmarAlteracaoComandaUseCase>().ExecuteAsync(new(acesso.EmpresaId,
                    acesso.ConversaId, estado.LinkId, true, Guid.Empty, "Cliente pelo link",
                    NivelAcesso.Operador, Automatico: true), ct);
            }
        }
        return resultado;
    });

    [HttpGet("eventos")]
    [EnableRateLimiting("comanda-stream")]
    public async Task Eventos(string token, CancellationToken ct)
    {
        EasyStock.Domain.Entities.Atendimento.LinkCardapioConversa acesso;
        try { acesso = await comanda.AcessoAsync(token, ct); }
        catch (LinkCardapioConversaIndisponivelException) { Response.StatusCode = 410; return; }
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-store, no-transform";
        Response.Headers["X-Accel-Buffering"] = "no";
        using var s = broker.SubscribeOperacao($"comanda:{Guid.NewGuid():N}", acesso.EmpresaId,
            e => e.StartsWith("conversa.", StringComparison.Ordinal) || e.StartsWith("pedido.", StringComparison.Ordinal));
        string? ultimo = null;
        while (!ct.IsCancellationRequested && relogio.GetUtcNow().UtcDateTime < acesso.ExpiraEm)
        {
            // O stream transmite somente o retrato da comanda autenticada, nunca o payload do broker do tenant.
            await using var escopo = escopos.CreateAsyncScope();
            string estado;
            try { estado = JsonSerializer.Serialize(await escopo.ServiceProvider.GetRequiredService<ComandaClienteUseCase>().ObterAsync(token, ct), ComandaClienteUseCase.Json); }
            catch (LinkCardapioConversaIndisponivelException) { break; }
            if (estado != ultimo)
            {
                await Response.WriteAsync($"event: comanda\ndata: {estado}\n\n", ct);
                ultimo = estado;
            }
            else await Response.WriteAsync(": conectado\n\n", ct);
            await Response.Body.FlushAsync(ct);
            await s.Slot.Signal.WaitAsync(TimeSpan.FromSeconds(15), ct);
            while (s.Slot.Queue.TryDequeue(out _)) { }
        }
    }

    private async Task<IActionResult> Tratar(Func<Task<object>> acao)
    {
        Response.Headers.CacheControl = "no-store";
        try { return Ok(await acao()); }
        catch (LinkCardapioConversaIndisponivelException e) { return Problem(e.Message, statusCode: 410); }
        catch (ComandaClienteConflitoException e) { return Problem(e.Message, statusCode: 409); }
        catch (UseCaseValidationException e) { return Problem(e.Message, statusCode: 400); }
        catch (RegraDeDominioVioladaException e) { return Problem(e.Message, statusCode: 422); }
    }
}
public sealed record SalvarComandaClienteBody(int Revisao, RascunhoComandaCliente Rascunho);
public sealed record EnviarComandaClienteBody(int Revisao);
