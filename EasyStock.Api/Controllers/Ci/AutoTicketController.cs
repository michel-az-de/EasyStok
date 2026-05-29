using System.Security.Cryptography;
using System.Text;
using EasyStock.Api.Services.Helpdesk;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Api.Controllers.Ci;

/// <summary>
/// Endpoint para criação automática de tickets de helpdesk a partir de falhas
/// em CI / smoke pos-deploy / runtime health check. Autenticacao via header
/// X-Ci-Key (env Ci:AutoTicketKey). Os tickets pousam na empresa interna
/// configurada em Ci:OwnerEmpresaId (a empresa-mãe do EasyStok recebe esses
/// tickets de dev — não os tenants finais).
///
/// Idempotencia diaria: o caller envia um "signature" deterministico (ex:
/// sha256(test_name + date_utc)). Se já existe um ticket aberto/em-atendimento
/// com mesmo signature hoje, anexamos comentario em vez de criar duplicado.
/// Evita 100 tickets do mesmo teste falhando 100x.
/// </summary>
[ApiController]
[Route("api/ci/tickets")]
[AllowAnonymous]
public class AutoTicketController(
    EasyStockDbContext db,
    HelpdeskTicketService ticketService,
    IConfiguration configuration,
    ILogger<AutoTicketController> log) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> CreateOrAttach(
        [FromBody] AutoTicketRequest req,
        CancellationToken ct)
    {
        if (req == null) return BadRequest(new { error = "payload obrigatorio" });

        var configuredKey = configuration["Ci:AutoTicketKey"];
        if (string.IsNullOrWhiteSpace(configuredKey))
        {
            log.LogWarning("Ci:AutoTicketKey nao configurado — /api/ci/tickets rejeitando tudo");
            return StatusCode(503, new { error = "auto-ticket desativado no servidor" });
        }

        var providedKey = Request.Headers["X-Ci-Key"].ToString();
        if (!ConstantTimeEquals(providedKey, configuredKey))
            return Unauthorized(new { error = "X-Ci-Key invalido" });

        var ownerRaw = configuration["Ci:OwnerEmpresaId"];
        if (!Guid.TryParse(ownerRaw, out var empresaId))
        {
            log.LogWarning("Ci:OwnerEmpresaId nao configurado");
            return StatusCode(503, new { error = "owner empresa nao configurado" });
        }

        if (string.IsNullOrWhiteSpace(req.Signature)
            || string.IsNullOrWhiteSpace(req.Titulo)
            || string.IsNullOrWhiteSpace(req.Origin))
        {
            return BadRequest(new { error = "signature, titulo e origin obrigatorios" });
        }

        var prioridade = ResolvePrioridade(req.Origin);
        var nivel = NivelAtendimento.N3; // Dev recebe N3 por default
        var signatureShort = req.Signature.Length > 16 ? req.Signature[..16] : req.Signature;
        var titlePrefix = $"[CI {signatureShort}] ";

        // Idempotencia: procura ticket aberto hoje com mesmo signature.
        // Status terminais (Resolvido/Fechado) NAO entram no filtro — uma nova
        // ocorrencia depois do fechamento gera um novo ticket (esperado: regressao).
        var hoje = DateTime.UtcNow.Date;
        var existente = await db.AdminTickets
            .Where(t => t.EmpresaId == empresaId
                     && t.Categoria == TicketCategoria.BugFixDev
                     && t.CriadoEm >= hoje
                     && t.Status != TicketStatus.Resolvido
                     && t.Status != TicketStatus.Fechado
                     && t.Titulo.StartsWith(titlePrefix))
            .FirstOrDefaultAsync(ct);

        if (existente != null)
        {
            // Anexa comentario interno sinalizando reincidencia
            var historico = TicketHistorico.Criar(
                existente.Id,
                autorId: null,
                TicketAcaoHistorico.Comentario,
                metadadosJson: System.Text.Json.JsonSerializer.Serialize(new
                {
                    reincidencia = true,
                    origin = req.Origin,
                    contexto = req.Contexto,
                    em = DateTime.UtcNow
                }));
            db.TicketHistoricos.Add(historico);
            existente.AlteradoEm = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            log.LogInformation("Auto-ticket reincidente {TicketId} (signature {Signature}, origin {Origin})",
                existente.Id, req.Signature, req.Origin);
            return Ok(new
            {
                ticketId = existente.Id,
                created = false,
                message = "ticket existente recebeu comentario de reincidencia"
            });
        }

        var ticket = await ticketService.AbrirAsync(new AbrirAdminTicketCommand(
            EmpresaId: empresaId,
            Titulo: titlePrefix + Truncate(req.Titulo, 200 - titlePrefix.Length),
            Descricao: BuildDescricao(req),
            Categoria: TicketCategoria.BugFixDev,
            Prioridade: prioridade,
            Nivel: nivel), ct);

        log.LogInformation("Auto-ticket criado {TicketId} (signature {Signature}, origin {Origin}, prioridade {Prioridade})",
            ticket.Id, req.Signature, req.Origin, prioridade);

        return StatusCode(201, new
        {
            ticketId = ticket.Id,
            created = true,
            prioridade = prioridade.ToString()
        });
    }

    private static TicketPrioridade ResolvePrioridade(string origin) =>
        origin.ToLowerInvariant() switch
        {
            "runtime" => TicketPrioridade.Critica,
            "smoke"   => TicketPrioridade.Alta,
            _         => TicketPrioridade.Normal
        };

    private static string BuildDescricao(AutoTicketRequest req)
    {
        var sb = new StringBuilder();
        sb.AppendLine(req.Descricao ?? "(sem descricao)");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine($"Origin: {req.Origin}");
        sb.AppendLine($"Signature: {req.Signature}");
        sb.AppendLine($"Detectado em: {DateTime.UtcNow:O}");
        if (!string.IsNullOrWhiteSpace(req.Contexto))
        {
            sb.AppendLine();
            sb.AppendLine("Contexto:");
            sb.AppendLine(req.Contexto);
        }
        return sb.ToString();
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max];

    /// <summary>
    /// Compara strings em tempo constante pra mitigar timing side-channels
    /// na validacao da chave de CI.
    /// </summary>
    private static bool ConstantTimeEquals(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
        var aBytes = Encoding.UTF8.GetBytes(a);
        var bBytes = Encoding.UTF8.GetBytes(b);
        if (aBytes.Length != bBytes.Length) return false;
        return CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
    }
}

/// <summary>
/// Payload do POST /api/ci/tickets. Signature deve ser deterministico
/// (mesmo input → mesma signature) pra que ocorrencias do mesmo problema
/// no mesmo dia sejam coalescadas em um unico ticket.
/// </summary>
public sealed record AutoTicketRequest(
    string Origin,
    string Signature,
    string Titulo,
    string? Descricao,
    string? Contexto);
