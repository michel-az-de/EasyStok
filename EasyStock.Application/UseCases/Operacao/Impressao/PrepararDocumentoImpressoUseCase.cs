using System.Security.Cryptography;
using System.Text;
using EasyStock.Application.Ports.Output.Persistence.Operacao;
using EasyStock.Domain.Entities.Operacao;

namespace EasyStock.Application.UseCases.Operacao.Impressao;

public sealed record PrepararDocumentoImpressoInput(Guid EmpresaId, Guid PedidoId, Guid SolicitacaoId,
    Guid? UsuarioId, string Documento, string Modelo, string Codigo, string Perfil, bool LogoTermica,
    string Html, string Versao, Guid? OriginalId = null);

public sealed class PrepararDocumentoImpressoUseCase(IImpressaoPendenteRepository repo,
    IPedidoRepository pedidos, IUnitOfWork unitOfWork, TimeProvider relogio)
{
    public async Task<DocumentoImpressoDto?> ExecuteAsync(PrepararDocumentoImpressoInput input, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(input.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(input.PedidoId, "PedidoId");
        UseCaseGuards.EnsureNotEmpty(input.SolicitacaoId, "SolicitacaoId");
        if (input.Perfil.Length is < 1 or > 80 || input.Html.Length > 12_000_000)
            throw new UseCaseValidationException("Perfil ou conteúdo de impressão inválido.");
        var intencao = Hash($"{input.PedidoId}|{input.Documento}|{input.Modelo}|{input.Codigo}|{input.Perfil}|{input.LogoTermica}|{input.OriginalId}");
        var anterior = await repo.GetBySolicitacaoAsync(input.EmpresaId, input.SolicitacaoId, ct);
        if (anterior is not null) return Conferir(anterior, intencao, input.EmpresaId);
        var pedido = await pedidos.GetByIdAsync(input.EmpresaId, input.PedidoId);
        if (pedido is null || pedido.EmpresaId != input.EmpresaId) return null;
        ImpressaoPendente? original = null;
        if (input.OriginalId is { } originalId)
        {
            original = await repo.GetByIdAsync(input.EmpresaId, originalId, ct);
            if (original is null || original.EmpresaId != input.EmpresaId || original.PedidoId != input.PedidoId || original.Html is null) return null;
        }
        var html = original?.Html ?? input.Html;
        var i = ImpressaoPendente.PrepararDocumento(input.EmpresaId, pedido.LojaId, pedido.Id,
            input.SolicitacaoId, input.UsuarioId, original?.Documento ?? input.Documento,
            original?.Modelo ?? input.Modelo, original?.Codigo ?? input.Codigo, input.Perfil,
            original?.Versao ?? input.Versao, html, original?.Hash ?? Hash(html), intencao,
            relogio.GetUtcNow().UtcDateTime, original?.OriginalId ?? original?.Id);
        await repo.AddAsync(i, ct);
        try { await unitOfWork.CommitAsync(); }
        catch (Exception ex) when (unitOfWork.EhViolacaoDeUnicidade(ex))
        {
            unitOfWork.DescartarAlteracoesPendentes();
            var vencedora = await repo.GetBySolicitacaoAsync(input.EmpresaId, input.SolicitacaoId, ct);
            if (vencedora is null) throw;
            return Conferir(vencedora, intencao, input.EmpresaId);
        }
        return DocumentoImpressoDto.De(i);
    }

    private static string Hash(string valor) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(valor)));
    private static DocumentoImpressoDto Conferir(ImpressaoPendente i, string intencao, Guid empresa)
    {
        if (i.EmpresaId != empresa) throw new UseCaseValidationException("Impressão não encontrada nesta empresa.");
        if (i.Intencao != intencao) throw new UseCaseConcurrencyException("Esta solicitação já foi usada para outra impressão. Abra uma nova prévia.");
        return DocumentoImpressoDto.De(i);
    }
}
