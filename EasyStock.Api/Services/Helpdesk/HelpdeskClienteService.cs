using EasyStock.Api.Utilities;
using EasyStock.Application.Ports.Output;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

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
        if (!currentUser.TemPermissao(Permissao.RevelarPiiCliente))
            throw new UnauthorizedAccessException("Sem permissao para revelar dados de cliente.");
        if (string.IsNullOrWhiteSpace(cmd.Motivo) || cmd.Motivo.Trim().Length < 10)
            throw new InvalidOperationException("Motivo obrigatorio (minimo 10 caracteres).");

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
