using EasyStock.Application.UseCases.Lotes;
using LoteEntity = EasyStock.Domain.Entities.Lote;

namespace EasyStock.Application.UseCases.CriarLote;

public sealed record CriarLoteItemInput(
    [property: Required][property: MaxLength(150)] string Nome,
    int Quantidade,
    Guid? ProdutoId = null,
    [property: MaxLength(16)] string? Emoji = null,
    [property: MaxLength(32)] string? Unidade = null,
    int? PesoG = null,
    int? ValidadeDias = null,
    string? FotoUrl = null);

public sealed record CriarLoteCommand(
    [property: Required] Guid EmpresaId,
    Guid? LojaId = null,
    DateTime? DataProducao = null,
    [property: MaxLength(40)] string? CodigoCustom = null,
    Guid? OperadorUserId = null,
    [property: MaxLength(120)] string? OperadorNome = null,
    string? Observacoes = null,
    string? FotoUrl = null,
    [property: MaxLength(20)] string? Origem = "web",
    [property: MaxLength(64)] string? MobileBatchId = null,
    IReadOnlyList<CriarLoteItemInput>? Itens = null);

/// <summary>
/// Cria um lote — gera código sequencial do dia (LOT-YYMMDD-NNN) se não vier
/// custom. Não gera etiquetas ainda — só ao Finalizar.
/// </summary>
public class CriarLoteUseCase(
    ILoteRepository repo,
    IProdutoRepository produtoRepo,
    IUnitOfWork uow,
    ILogger<CriarLoteUseCase> logger)
{
    public async Task<LoteResult> ExecuteAsync(CriarLoteCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);

        var dataProd = cmd.DataProducao ?? DateTime.UtcNow;
        var data = DateOnly.FromDateTime(dataProd);

        string codigo;
        if (!string.IsNullOrWhiteSpace(cmd.CodigoCustom))
        {
            codigo = cmd.CodigoCustom.Trim();
            var existe = await repo.FindByCodigoAsync(cmd.EmpresaId, codigo);
            if (existe != null)
                throw new UseCaseValidationException($"Código '{codigo}' já existe.");
        }
        else
        {
            var seq = await repo.GetNextSequencialDoDiaAsync(cmd.EmpresaId, data);
            codigo = $"LOT-{data:yyMMdd}-{seq:D3}";
        }

        var lote = LoteEntity.Criar(cmd.EmpresaId, codigo, dataProd, cmd.LojaId);
        lote.OperadorUserId = cmd.OperadorUserId;
        lote.OperadorNome = cmd.OperadorNome;
        lote.Observacoes = cmd.Observacoes;
        lote.FotoUrl = cmd.FotoUrl;
        lote.Origem = cmd.Origem;
        lote.MobileBatchId = cmd.MobileBatchId;

        if (cmd.Itens != null)
        {
            // C2 (RDC 727/2022): consulta tipo de embalagem dos produtos referenciados
            // em UMA query, para validar peso obrigatorio condicional. Itens com
            // ProdutoId null nao podem existir (ver guard abaixo - R1).
            var produtoIds = cmd.Itens
                .Where(i => i.ProdutoId.HasValue)
                .Select(i => i.ProdutoId!.Value)
                .Distinct()
                .ToList();
            var tipoEmbalagemMap = produtoIds.Count > 0
                ? await produtoRepo.GetTipoEmbalagemMapAsync(cmd.EmpresaId, produtoIds)
                : new Dictionary<Guid, TipoEmbalagem>();

            foreach (var input in cmd.Itens)
            {
                if (input.Quantidade <= 0)
                    throw new UseCaseValidationException("Quantidade deve ser maior que zero.");

                if (string.IsNullOrWhiteSpace(input.Nome))
                    throw new UseCaseValidationException("Nome do item é obrigatório.");

                // R1: ProdutoId obrigatório — sem vínculo, não há como validar
                // TipoEmbalagem e o bypass silencioso da RDC 727 fica possível.
                if (!input.ProdutoId.HasValue)
                    throw new UseCaseValidationException(
                        $"Item '{input.Nome}' precisa estar vinculado a um produto cadastrado.");

                // R5: peso obrigatório APENAS quando produto é Embalado (RDC 727/2022).
                if (tipoEmbalagemMap.TryGetValue(input.ProdutoId.Value, out var tipoEmb)
                    && tipoEmb == TipoEmbalagem.Embalado
                    && (!input.PesoG.HasValue || input.PesoG.Value <= 0))
                {
                    throw new UseCaseValidationException(
                        $"Peso por unidade obrigatório para o item '{input.Nome}' " +
                        $"(produto está marcado como Embalado — RDC 727/2022).");
                }

                var expira = input.ValidadeDias.HasValue ? lote.DataProducao.AddDays(input.ValidadeDias.Value) : (DateTime?)null;
                lote.Itens.Add(new LoteItem
                {
                    Id = Guid.NewGuid(),
                    LoteId = lote.Id,
                    ProdutoId = input.ProdutoId,
                    Nome = input.Nome.Trim(),
                    Emoji = input.Emoji,
                    Unidade = input.Unidade,
                    Quantidade = input.Quantidade,
                    PesoG = input.PesoG,
                    ValidadeDias = input.ValidadeDias,
                    ExpiraEm = expira,
                    FotoUrl = input.FotoUrl,
                    CriadoEm = DateTime.UtcNow
                });
            }
        }

        await repo.AddAsync(lote);
        await uow.CommitAsync();

        logger.LogInformation("Lote {Id} ({Codigo}) criado com {N} item(s).", lote.Id, codigo, lote.Itens.Count);
        return Map(lote);
    }

    internal static LoteResult Map(LoteEntity l)
    {
        var conferidas = l.Etiquetas.Count(e => e.Status == "conferida");
        var divergentes = l.Etiquetas.Count(e => e.Status == "divergente");
        return new LoteResult(
            l.Id, l.EmpresaId, l.LojaId, l.Codigo, l.Status, l.DataProducao,
            l.OperadorUserId, l.OperadorNome, l.Observacoes, l.FotoUrl,
            l.Origem, l.MobileBatchId,
            l.Itens.Count, l.TotalUnidades,
            conferidas, divergentes,
            l.CriadoEm, l.AlteradoEm, l.FinalizadoEm);
    }
}
