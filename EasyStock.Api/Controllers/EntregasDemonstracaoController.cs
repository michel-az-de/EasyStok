using System.Text.Encodings.Web;
using EasyStock.Application.UseCases.Atendimento.Entregas;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/entregas-demonstracao")]
public sealed class EntregasDemonstracaoController(RastreioLogisticaLink links, ConfiguracaoLogistica config,
    ObterRastreioLogisticaUseCase rastreio, EasyStockDbContext db) : ControllerBase
{
    [HttpGet("{token}")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Obter(string token, CancellationToken ct)
    {
        var id = links.Validar(token);
        if (id is null) return NotFound();
        db.SetMobileTenantContext(config.EmpresaDemonstracaoId);
        var dados = await rastreio.ExecuteAsync(config.EmpresaDemonstracaoId, id.Value, ct);
        if (dados is null) return NotFound();
        static string Rotulo(string tipo) => tipo switch
        {
            "agendado" => "Aguardando coleta", "a_caminho" => "Seu pedido está a caminho",
            "entregue" => "Pedido entregue", "devolvendo" => "Retornando à loja", "devolvido" => "Recebido de volta na loja",
            "cancelado" => "Entrega cancelada", "falha" => "A equipe está conferindo a entrega",
            "pagamento_confirmado" => "Transporte confirmado", _ => "Atualização registrada"
        };
        var titulo = HtmlEncoder.Default.Encode(Rotulo(dados.Status));
        var eventos = string.Join("", dados.Eventos.Select(e => $"<li>{HtmlEncoder.Default.Encode(Rotulo(e.Tipo))}</li>"));
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        Response.Headers["Content-Security-Policy"] = "default-src 'none'; style-src 'unsafe-inline'; base-uri 'none'; frame-ancestors 'none'";
        return Content($$"""
            <!doctype html><html lang="pt-BR"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
            <title>Acompanhar entrega · Demonstração</title><style>body{margin:0;background:#faf6ef;color:#462d1b;font:18px/1.6 system-ui}main{max-width:560px;margin:10vh auto;padding:32px}small{display:block;color:#725139}h1{font-size:32px;line-height:1.2}ol{padding-left:24px}li{padding:10px 0}a{color:#854000}aside{padding:16px;background:#fff0c2;border-radius:12px}</style>
            <main><small>ACOMPANHE SUA ENTREGA</small><h1>{{titulo}}</h1><aside>Este é um acompanhamento de demonstração. Os eventos dos transportadores são simulados.</aside><ol>{{eventos}}</ol><p><a href="">Atualizar acompanhamento</a></p><small>O link expira em sete dias. Não exibe endereço, telefone ou valores.</small></main></html>
            """, "text/html; charset=utf-8");
    }
}
