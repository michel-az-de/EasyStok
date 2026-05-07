using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Storage;
using EasyStock.Application.UseCases.GerenciarUploads;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Api.Services.Helpdesk;

/// <summary>
/// Upload de anexos de ticket. Reusa IFileStorage e UploadSecurityValidator.
/// Bucket path: tickets/{empresaId}/{ticketId}/{anexoId}{ext}.
/// Visibilidade default: privado (IsPublico=false). Limite: 10 MB por arquivo.
/// </summary>
public sealed class HelpdeskAnexoService(
    EasyStockDbContext db,
    ICurrentUserAccessor currentUser,
    IFileStorage fileStorage)
{
    public const long TamanhoMaximoBytes = 10 * 1024 * 1024; // 10 MB
    public const int MaxAnexosPorTicketAdmin = 10;
    public const int MaxAnexosPorMensagemCliente = 5;

    public async Task<TicketAnexo> AnexarAsync(AnexarArquivoCommand cmd, CancellationToken ct = default)
    {
        if (cmd.Conteudo is null || cmd.Conteudo.Length == 0)
            throw new InvalidOperationException("Conteudo do anexo vazio.");
        if (cmd.Conteudo.Length > TamanhoMaximoBytes)
            throw new InvalidOperationException($"Anexo excede limite de {TamanhoMaximoBytes / (1024 * 1024)} MB.");

        var nomeSeguro = UploadSecurityValidator.SanitizeFileName(cmd.NomeArquivo);
        UploadSecurityValidator.EnsureValidMime(cmd.ContentType);

        var ticket = await db.AdminTickets
            .Where(t => t.Id == cmd.TicketId)
            .Select(t => new { t.Id, t.EmpresaId, t.Status })
            .FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException("Ticket nao encontrado.");

        var anexosExistentes = await db.TicketAnexos.CountAsync(a => a.TicketId == cmd.TicketId, ct);
        var limite = cmd.IsAdmin ? MaxAnexosPorTicketAdmin : MaxAnexosPorMensagemCliente;
        if (anexosExistentes >= limite)
            throw new InvalidOperationException($"Limite de {limite} anexos por ticket atingido.");

        var anexoId = Guid.NewGuid();
        var ext = Path.GetExtension(nomeSeguro);
        var bucketPath = $"tickets/{ticket.EmpresaId:N}/{ticket.Id:N}";
        var fileName = $"{anexoId:N}{ext}";

        var stored = await fileStorage.UploadAsync(new FileUploadRequest(
            BucketPath: bucketPath,
            FileName: fileName,
            ContentType: cmd.ContentType,
            Content: cmd.Conteudo,
            IsPublic: false), ct);

        var anexo = TicketAnexo.Criar(
            ticketId: ticket.Id,
            mensagemId: cmd.MensagemId,
            nomeArquivo: nomeSeguro,
            contentType: cmd.ContentType,
            tamanhoBytes: stored.Size,
            storageKey: stored.StorageKey,
            url: stored.Url,
            isPublico: false,
            enviadoPorId: currentUser.UsuarioId,
            isAdmin: cmd.IsAdmin);
        anexo.Id = anexoId;

        db.TicketAnexos.Add(anexo);
        db.TicketHistoricos.Add(TicketHistorico.Criar(
            ticket.Id, currentUser.UsuarioId, TicketAcaoHistorico.AnexoAdicionado,
            valorDepois: nomeSeguro));

        await db.CommitAsync();
        return anexo;
    }
}
