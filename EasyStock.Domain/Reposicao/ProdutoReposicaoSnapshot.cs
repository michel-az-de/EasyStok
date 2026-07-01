namespace EasyStock.Domain.Reposicao;

/// <summary>
/// Projeção por-produto que alimenta o <see cref="EasyStock.Domain.Services.AnalisadorReposicao"/>
/// (função pura). A infraestrutura monta este snapshot (FROM Produtos LEFT JOIN ItensEstoque
/// vigentes — ADR-0039), trazendo os limiares BRUTOS (produto/categoria/config) para o domínio
/// resolver a hierarquia; assim a regra R1 fica testável sem tocar no banco.
/// </summary>
/// <param name="QuantidadeVigente">Soma das quantidades dos lotes NÃO vencidos do produto (R1/R2). 0 quando nunca estocado.</param>
/// <param name="VelocidadeMediaDia">Velocidade média de saída diária (unidades/dia), apenas Natureza==Venda (R3).</param>
/// <param name="DiasHistoricoVelocidade">Dias de histórico de saída disponíveis (governa a confiança, R3).</param>
/// <param name="LeadTimeDias">Lead time efetivo do produto (fornecedor do lote vigente, ou padrão da loja).</param>
/// <param name="ValidadeMediaDiasRestantes">Dias restantes até o vencimento médio dos lotes vigentes; null se sem validade.</param>
public sealed record ProdutoReposicaoSnapshot(
    Guid ProdutoId,
    Guid? VariacaoId,
    string Nome,
    decimal QuantidadeVigente,
    int? ProdutoMinima,
    int? ProdutoCritica,
    int? CategoriaMinima,
    int? CategoriaCritica,
    int? ConfigMinima,
    int? ConfigCritica,
    decimal VelocidadeMediaDia,
    int DiasHistoricoVelocidade,
    int LeadTimeDias,
    int TamanhoLote,
    int? ValidadeMediaDiasRestantes,
    Guid? FornecedorId,
    decimal CustoUnitario = 0m);
