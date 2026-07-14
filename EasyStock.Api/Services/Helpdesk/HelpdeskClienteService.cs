using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Api.Services.Helpdesk;

public sealed record EmpresaPreviewResultado(
    Guid Id,
    string Nome,
    string? DocumentoExibicao,
    string? EmailExibicao,
    string? PlanoNome,
    bool Mascarado,
    string? MotivoRevelacao);

/// <summary>
/// Retorna dados resumidos da empresa para o card de cliente em tickets.
/// Por default tudo mascarado via PiiMaskingHelper. Revelar exige permissao
/// RevelarPiiCliente + motivo (LGPD), grava AdminAuditLog + TicketHistorico.
/// </summary>
public sealed class HelpdeskClienteService(
    EasyStockDbContext db,
    ICurrentUserAccessor currentUser,
    AdminAuditService audit)
{
    public async Task<EmpresaPreviewResultado> PreviewMascaradoAsync(Guid empresaId, CancellationToken ct = default)
    {
        var dados = await CarregarAsync(empresaId, ct);
        return new EmpresaPreviewResultado(
            dados.Id,
            dados.Nome,
            MascararDocumento(dados.Documento),
            null, // email vem de outra fonte (Usuario/Loja); placeholder ate integrar
            dados.PlanoNome,
            Mascarado: true,
            MotivoRevelacao: null);
    }

    public async Task<EmpresaPreviewResultado> RevelarAsync(RevelarClienteCommand cmd, CancellationToken ct = default)
    {
        // BUG-05/ADR-0030: gate unico = policy SuperAdmin no AdminEmpresaPreviewController (unico
        // caller de RevelarAsync). A checagem redundante de Permissao.RevelarPiiCliente foi
        // removida para alinhar a um so portao (SuperAdmin-only, decisao Felipe).
        if (string.IsNullOrWhiteSpace(cmd.Motivo) || cmd.Motivo.Trim().Length < 10)
            throw new InvalidOperationException("Motivo obrigatorio (minimo 10 caracteres).");

        // BOLA (ADM-BOLA-1): quando ha ticket de contexto, ele DEVE pertencer a empresa cujo dado
        // sera revelado. Validado ANTES de carregar/retornar a PII: sem esta amarracao, um SuperAdmin
        // no ticket do Tenant A revelaria a PII do Tenant B e ainda gravaria o TicketHistorico no
        // ticket errado (trilha de auditoria corrompida). Ticket permanece opcional (decisao Felipe):
        // valida-se somente quando fornecido; reveal sem ticket segue permitido.
        if (cmd.TicketIdContexto.HasValue)
        {
            var ticketEmpresaId = await db.AdminTickets.AsNoTracking()
                .Where(t => t.Id == cmd.TicketIdContexto.Value)
                .Select(t => (Guid?)t.EmpresaId)
                .FirstOrDefaultAsync(ct);
            if (ticketEmpresaId is null)
                throw new KeyNotFoundException("Ticket de contexto nao encontrado.");
            if (ticketEmpresaId.Value != cmd.EmpresaId)
                throw new UnauthorizedAccessException("Ticket de contexto nao pertence a empresa informada.");
        }

        var dados = await CarregarAsync(cmd.EmpresaId, ct);

        await audit.LogAsync(
            acao: "ClienteRevelado",
            detalhes: $"EmpresaId={cmd.EmpresaId}, TicketContexto={cmd.TicketIdContexto}",
            tenantId: cmd.EmpresaId,
            motivo: cmd.Motivo.Trim(),
            entidadeAfetadaId: cmd.EmpresaId);

        if (cmd.TicketIdContexto.HasValue)
        {
            db.TicketHistoricos.Add(TicketHistorico.Criar(
                cmd.TicketIdContexto.Value,
                currentUser.UsuarioId,
                TicketAcaoHistorico.ClienteRevelado,
                valorDepois: cmd.EmpresaId.ToString()));
            await db.CommitAsync();
        }

        return new EmpresaPreviewResultado(
            dados.Id,
            dados.Nome,
            dados.Documento,
            null,
            dados.PlanoNome,
            Mascarado: false,
            MotivoRevelacao: cmd.Motivo.Trim());
    }

    private async Task<DadosEmpresa> CarregarAsync(Guid empresaId, CancellationToken ct)
    {
        var empresa = await db.Empresas.AsNoTracking().FirstOrDefaultAsync(e => e.Id == empresaId, ct)
            ?? throw new KeyNotFoundException("Empresa nao encontrada.");

        var planoNome = await db.AssinaturasEmpresa.AsNoTracking()
            .Where(a => a.EmpresaId == empresaId && a.Status == StatusAssinatura.Ativa)
            .OrderByDescending(a => a.DataInicio)
            .Select(a => a.Plano!.Nome)
            .FirstOrDefaultAsync(ct);

        return new DadosEmpresa(empresa.Id, empresa.Nome, empresa.Documento, planoNome);
    }

    private static string? MascararDocumento(string? doc)
    {
        if (string.IsNullOrWhiteSpace(doc)) return null;
        var digitos = new string(doc.Where(char.IsDigit).ToArray());
        if (digitos.Length < 6) return "***";
        if (digitos.Length == 11)
            return $"{digitos[..3]}.***.***-{digitos[^2..]}";
        if (digitos.Length == 14)
            return $"{digitos[..2]}.***.***/****-{digitos[^2..]}";
        return $"{digitos[..2]}***{digitos[^2..]}";
    }

    private sealed record DadosEmpresa(Guid Id, string Nome, string? Documento, string? PlanoNome);
}
