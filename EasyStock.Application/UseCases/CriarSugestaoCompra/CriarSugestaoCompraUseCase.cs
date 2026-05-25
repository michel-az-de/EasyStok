using EasyStock.Application.Ports.Output.Integration;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Enums;
using Microsoft.Extensions.Logging;
using FornecedorEntity = EasyStock.Domain.Entities.Fornecedor;
using PedidoFornecedorEntity = EasyStock.Domain.Entities.PedidoFornecedor;
using PedidoFornecedorItemEntity = EasyStock.Domain.Entities.PedidoFornecedorItem;

namespace EasyStock.Application.UseCases.CriarSugestaoCompra;

/// <summary>
/// Cria N PedidoFornecedor (1 por grupo) com seus itens, all-or-nothing via
/// IUnitOfWork.ExecuteInTransactionAsync. Se 1 fornecedor for invalido (cross-tenant,
/// inativo ou inexistente), nenhum PF e criado. Publica evento de integracao
/// (pedido_fornecedor.criado_via_calculadora) por PF criado — worker existente
/// processa Outbox para notificar fornecedores via WhatsApp/Email.
/// </summary>
public class CriarSugestaoCompraUseCase(
    IPedidoFornecedorRepository pedidoRepository,
    IFornecedorRepository fornecedorRepository,
    IPublicadorEventoIntegracao publicadorEvento,
    IUnitOfWork unitOfWork,
    ILogger<CriarSugestaoCompraUseCase> logger)
{
    public async Task<CriarSugestaoCompraResult> ExecuteAsync(
        CriarSugestaoCompraCommand command, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(command.EmpresaId);

        if (string.IsNullOrWhiteSpace(command.IdempotencyKey))
            throw new UseCaseValidationException("INVALID_IDEMPOTENCY_KEY", "Idempotency-Key obrigatorio.");

        if (command.Fornecedores.Count == 0)
            throw new UseCaseValidationException("EMPTY_REQUEST", "Pelo menos 1 fornecedor com itens e obrigatorio.");

        // Pre-validacao: todos os fornecedores existem, pertencem a empresa e estao ativos
        // (fail fast antes de comecar transacao)
        var fornecedoresValidados = new List<(FornecedorGrupoInput Grupo, FornecedorEntity Fornecedor)>(command.Fornecedores.Count);
        foreach (var grupo in command.Fornecedores)
        {
            UseCaseGuards.EnsureNotEmpty(grupo.FornecedorId, "FornecedorId");
            if (grupo.Itens.Count == 0)
                throw new UseCaseValidationException("EMPTY_GROUP", $"Fornecedor {grupo.FornecedorId} sem itens.");

            var fornecedor = await fornecedorRepository.GetByIdAsync(command.EmpresaId, grupo.FornecedorId)
                ?? throw new UseCaseValidationException(
                    "SUPPLIER_REQUIRED",
                    $"Fornecedor {grupo.FornecedorId} nao encontrado nesta empresa.",
                    new { fornecedorId = grupo.FornecedorId });

            if (!fornecedor.Ativo)
                throw new UseCaseValidationException(
                    "SUPPLIER_INACTIVE",
                    $"Fornecedor {fornecedor.Nome} esta inativo.",
                    new { fornecedorId = fornecedor.Id, fornecedorNome = fornecedor.Nome });

            foreach (var item in grupo.Itens)
            {
                if (item.Quantidade <= 0)
                    throw new UseCaseValidationException("INVALID_QUANTITY",
                        $"Item {item.Nome}: quantidade deve ser maior que zero.");
                if (item.CustoUnitario < 0)
                    throw new UseCaseValidationException("INVALID_COST",
                        $"Item {item.Nome}: custo nao pode ser negativo.");
            }

            fornecedoresValidados.Add((grupo, fornecedor));
        }

        var pedidosCriados = new List<PedidoCriadoResult>();

        await unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var agora = DateTime.UtcNow;
            foreach (var (grupo, fornecedor) in fornecedoresValidados)
            {
                decimal valorEstimado = grupo.Itens.Sum(i => i.Quantidade * i.CustoUnitario);

                var pf = new PedidoFornecedorEntity
                {
                    Id = Guid.NewGuid(),
                    EmpresaId = command.EmpresaId,
                    FornecedorId = fornecedor.Id,
                    LojaId = command.LojaId,
                    DataPedido = agora,
                    PrevisaoEntrega = fornecedor.LeadTimeEstimadoDias.HasValue
                        ? agora.AddDays(fornecedor.LeadTimeEstimadoDias.Value)
                        : null,
                    ValorEstimado = valorEstimado,
                    Status = StatusPedidoFornecedor.Aberto,
                    Canal = command.Canal ?? "calculadora",
                    Observacoes = command.Observacoes,
                    CriadoEm = agora,
                    AlteradoEm = agora
                };

                await pedidoRepository.AddAsync(pf);

                foreach (var item in grupo.Itens)
                {
                    await pedidoRepository.AddItemAsync(new PedidoFornecedorItemEntity
                    {
                        Id = Guid.NewGuid(),
                        PedidoFornecedorId = pf.Id,
                        ProdutoId = item.InsumoId,
                        Nome = item.Nome,
                        Unidade = item.Unidade.ToString(),
                        Quantidade = item.Quantidade,
                        QuantidadeRecebida = 0m,
                        CustoUnitario = item.CustoUnitario,
                        Observacao = item.Observacao,
                        CriadoEm = agora
                    });
                }

                // Publica evento integracao (Outbox transacional): worker existente notifica fornecedor
                await publicadorEvento.PublicarAsync(
                    empresaId: command.EmpresaId,
                    tipoEvento: "pedido_fornecedor.criado_via_calculadora",
                    aggregateType: "PedidoFornecedor",
                    aggregateId: pf.Id,
                    payload: new
                    {
                        pedidoFornecedorId = pf.Id,
                        fornecedorId = fornecedor.Id,
                        fornecedorNome = fornecedor.Nome,
                        fornecedorEmail = fornecedor.Email,
                        valorEstimado,
                        itens = grupo.Itens.Select(i => new
                        {
                            i.InsumoId,
                            i.Nome,
                            i.Quantidade,
                            unidade = i.Unidade.ToString(),
                            i.CustoUnitario
                        })
                    },
                    payloadSchemaVersion: 1,
                    correlationId: command.IdempotencyKey,
                    causationEventId: null,
                    ct: innerCt);

                pedidosCriados.Add(new PedidoCriadoResult(
                    PedidoFornecedorId: pf.Id,
                    FornecedorId: fornecedor.Id,
                    FornecedorNome: fornecedor.Nome,
                    ValorEstimado: valorEstimado,
                    ItemCount: grupo.Itens.Count));
            }

            await unitOfWork.CommitAsync();
        }, ct);

        logger.LogInformation(
            "Sugestao de compra: empresa {EmpresaId} criou {PedidosCount} PFs via calculadora (idempotency {Key}).",
            command.EmpresaId, pedidosCriados.Count, command.IdempotencyKey);

        return new CriarSugestaoCompraResult(pedidosCriados);
    }
}
