using System.Text.Json;
using EasyStock.Api.Services.Operacao;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Comanda;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Exceptions.Storefront;
using EasyStock.Application.UseCases.Atendimento;
using Npgsql;
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
    [EnableRateLimiting("comanda-leitura")]
    public Task<IActionResult> Obter(string token, CancellationToken ct) => Tratar(async () => await comanda.ObterAsync(token, ct));

    [HttpGet("cardapio")]
    [EnableRateLimiting("comanda-leitura")]
    public Task<IActionResult> Cardapio(string token, CancellationToken ct) => Tratar(async () =>
        await cardapio.ExecuteAsync((await comanda.AcessoAsync(token, ct)).EmpresaId, ct));

    [HttpGet("janelas")]
    [EnableRateLimiting("comanda-leitura")]
    public Task<IActionResult> Janelas(string token, [FromQuery] Guid[]? itens, CancellationToken ct) => Tratar(async () =>
        await janelas.ExecuteAsync(new((await comanda.AcessoAsync(token, ct)).EmpresaId, null, null, itens ?? []), ct));

    [HttpPut("rascunho")]
    [EnableRateLimiting("comanda-rascunho")]
    public Task<IActionResult> Salvar(string token, [FromBody] SalvarComandaClienteBody body, CancellationToken ct) =>
        Tratar(async () => await comanda.SalvarAsync(token, body.Revisao, body.Rascunho, ct));

    [HttpPost("confirmar")]
    [EnableRateLimiting("comanda-envio")]
    public Task<IActionResult> Enviar(string token, [FromBody] EnviarComandaClienteBody body, CancellationToken ct) => Tratar(async () =>
    {
        EnvioComandaCliente resultado;
        try { resultado = await comanda.EnviarAsync(token, body.Revisao, ct); }
        catch (CepSemCoberturaException) { return await comanda.EncaminharEntregaAsync(token, body.Revisao, ct); }
        if (resultado.Situacao == "alteracao_aguardando_confirmacao")
        {
            var estado = await comanda.ObterAsync(token, ct);
            // Só acréscimos preservando pratos e observações passam direto. Trocas têm conferência humana.
            if (estado.Pedido is { } pedido && resultado.Total >= pedido.Total
                && (estado.Rascunho.Observacoes ?? "") == (pedido.Observacoes ?? "")
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

    [HttpPost("nova-edicao")]
    [EnableRateLimiting("comanda-envio")]
    public Task<IActionResult> NovaEdicao(string token, CancellationToken ct) =>
        Tratar(async () => await comanda.NovaEdicaoAsync(token, ct));

    [HttpPost("encaminhar-entrega")]
    [EnableRateLimiting("comanda-envio")]
    public Task<IActionResult> EncaminharEntrega(string token, [FromBody] EnviarComandaClienteBody body, CancellationToken ct) =>
        Tratar(async () => await comanda.EncaminharEntregaAsync(token, body.Revisao, ct));

    [HttpPost("pedir-ajuda")]
    [EnableRateLimiting("comanda-envio")]
    public Task<IActionResult> PedirAjuda(string token, CancellationToken ct) =>
        Tratar(async () => await comanda.PedirAjudaAsync(token, ct));

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
            Guid? pedidoId;
            string estado;
            await using (var escopo = escopos.CreateAsyncScope())
            {
                try
                {
                    var retrato = await escopo.ServiceProvider.GetRequiredService<ComandaClienteUseCase>().ObterAsync(token, ct);
                    pedidoId = retrato.Pedido?.PedidoId;
                    estado = JsonSerializer.Serialize(retrato, ComandaClienteUseCase.Json);
                }
                catch (LinkCardapioConversaIndisponivelException) { break; }
            }
            if (estado != ultimo)
            {
                await Response.WriteAsync($"event: comanda\ndata: {estado}\n\n", ct);
                ultimo = estado;
            }
            else await Response.WriteAsync(": conectado\n\n", ct);
            await Response.Body.FlushAsync(ct);
            // Relê quando o evento é desta comanda ou no batimento de 15 s, que cobre o que o evento não diz (#1719).
            var limite = relogio.GetUtcNow().AddSeconds(15);
            while (true)
            {
                var resta = limite - relogio.GetUtcNow();
                if (resta <= TimeSpan.Zero || !await s.Slot.Signal.WaitAsync(resta, ct)) break;
                if (s.Slot.Cancelled || EsvaziarEventos(s.Slot, acesso.ConversaId, pedidoId)) break;
            }
        }
    }

    /// <summary>
    /// Esvazia a fila e o sinal do ouvinte e diz se algum evento cita a conversa ou o pedido desta comanda. O
    /// ouvinte recebe os eventos de conversa e de pedido da loja inteira; reler o retrato a cada um custava dezenas
    /// de consultas por evento alheio, multiplicadas pelas comandas abertas (#1719).
    /// </summary>
    public static bool EsvaziarEventos(OperacaoEventBroker.ListenerSlot slot, Guid conversaId, Guid? pedidoId)
    {
        var conversa = conversaId.ToString();
        var pedido = pedidoId?.ToString();
        var relevante = false;
        // O sinal sai antes da fila: o broker enfileira e depois sinaliza, então evento que chega no meio fica
        // na fila com o sinal dele sobrando (um giro a mais), nunca na fila sem sinal.
        while (slot.Signal.CurrentCount > 0 && slot.Signal.Wait(0)) { }
        while (slot.Queue.TryDequeue(out var evento))
            relevante |= evento.Dados.Contains(conversa, StringComparison.OrdinalIgnoreCase)
                || (pedido is not null && evento.Dados.Contains(pedido, StringComparison.OrdinalIgnoreCase));
        return relevante;
    }

    private async Task<IActionResult> Tratar(Func<Task<object>> acao)
    {
        Response.Headers.CacheControl = "no-store";
        try { return Ok(await acao()); }
        catch (LinkCardapioConversaIndisponivelException e) { return Problem(e.Message, statusCode: 410); }
        catch (ComandaClienteConflitoException e) { return Problem(e.Message, statusCode: 409); }
        catch (TelefoneExigeVerificacaoException e) { return ExigirVerificacao(e.Message); }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: "23505", ConstraintName: "ix_clientes_empresa_telefone_hash" })
        { return ExigirVerificacao(new TelefoneExigeVerificacaoException().Message); }
        catch (UseCaseValidationException e) { return Problem(e.Message, statusCode: 400); }
        catch (RegraDeDominioVioladaException e) { return Problem(e.Message, statusCode: 422); }
        // #1716: quem lê é o cliente. O motivo técnico (provedor fora ou sem credencial) fica no log do caso de uso.
        catch (MercadoPagoIndisponivelException e)
        {
            return Problem(e.InnerException is MercadoPagoNaoConfiguradoException ? PagamentoOnlineSemProvedor : PagamentoOnlineInstavel, statusCode: 503);
        }
    }
    private ObjectResult ExigirVerificacao(string mensagem) => StatusCode(409, new { error = new { code = "TELEFONE_EXIGE_VERIFICACAO", message = mensagem } });

    private const string PagamentoOnlineSemProvedor =
        "O pagamento online está indisponível agora. Sua seleção continua salva: fale com a Casa para combinar o pagamento.";
    private const string PagamentoOnlineInstavel =
        "O pagamento online não respondeu agora. Sua seleção continua salva: tente de novo em instantes.";
}
public sealed record SalvarComandaClienteBody(int Revisao, RascunhoComandaCliente Rascunho);
public sealed record EnviarComandaClienteBody(int Revisao);
