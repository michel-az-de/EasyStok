using System;
using System.Collections.Generic;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Exceptions;
using EasyStock.Domain.ValueObjects;

namespace EasyStock.Domain.Entities
{
    public class ItemEstoque
    {
        public Guid Id { get; set; }
        public Guid EmpresaId { get; set; }
        public Guid ProdutoId { get; set; }
        public Guid? ProdutoVariacaoId { get; set; }

        public string? CodigoInterno { get; set; }
        public CodigoLote? CodigoLote { get; set; }
        public string? CodigoMarketplace { get; set; }
        public string? ChavePesquisa { get; set; }

        public string? VariacaoDescricao { get; set; }
        public string? Cor { get; set; }
        public string? Tamanho { get; set; }
        public string? DescricaoAnuncio { get; set; }

        public Dimensoes? DimensoesReais { get; set; }

        public string? FornecedorNome { get; set; }

        public Quantidade QuantidadeInicial { get; set; } = null!;
        public Quantidade QuantidadeAtual { get; set; } = null!;

        public Dinheiro CustoUnitario { get; set; } = null!;
        public Dinheiro? PrecoVendaSugerido { get; set; }

        public DateTime EntradaEm { get; set; }
        public Validade? ValidadeEm { get; set; }
        public DateTime? UltimaMovimentacaoEm { get; set; }

        public StatusItemEstoque Status { get; set; }
        public string? Observacoes { get; set; }

        public DateTime CriadoEm { get; set; }
        public DateTime AlteradoEm { get; set; }

        public Empresa? Empresa { get; set; }
        public Produto? Produto { get; set; }
        public ProdutoVariacao? ProdutoVariacao { get; set; }
        public ICollection<ItemVenda>? ItensVenda { get; set; }
        public ICollection<MovimentacaoEstoque>? Movimentacoes { get; set; }

        public static ItemEstoque CriarParaEntrada(
            Guid id,
            Guid empresaId,
            Produto produto,
            ProdutoVariacao? variacao,
            Quantidade quantidade,
            Dinheiro custoUnitario,
            Dinheiro? precoVendaSugerido,
            DateTime dataEntrada,
            string? codigoInterno,
            CodigoLote? codigoLote,
            string? codigoMarketplace,
            string? variacaoDescricao,
            string? cor,
            string? tamanho,
            string? descricaoAnuncio,
            Dimensoes? dimensoesReais,
            string? fornecedorNome,
            Validade? validade,
            string? observacoes,
            DateTime criadoEm)
        {
            var item = new ItemEstoque
            {
                Id = id,
                EmpresaId = empresaId,
                ProdutoId = produto.Id,
                ProdutoVariacaoId = variacao?.Id,
                CodigoInterno = NormalizarTexto(codigoInterno),
                CodigoLote = codigoLote,
                CodigoMarketplace = NormalizarTexto(codigoMarketplace),
                VariacaoDescricao = NormalizarTexto(variacaoDescricao) ?? variacao?.DescricaoComercial ?? variacao?.Nome,
                Cor = NormalizarTexto(cor) ?? variacao?.Cor,
                Tamanho = NormalizarTexto(tamanho) ?? variacao?.Tamanho,
                DescricaoAnuncio = NormalizarTexto(descricaoAnuncio),
                DimensoesReais = dimensoesReais ?? variacao?.DimensoesPadrao ?? produto.Dimensoes,
                FornecedorNome = NormalizarTexto(fornecedorNome),
                QuantidadeInicial = quantidade,
                QuantidadeAtual = quantidade,
                CustoUnitario = custoUnitario,
                PrecoVendaSugerido = precoVendaSugerido ?? produto.PrecoReferencia,
                EntradaEm = dataEntrada,
                ValidadeEm = validade,
                UltimaMovimentacaoEm = dataEntrada,
                Status = StatusItemEstoque.Ativo,
                Observacoes = NormalizarTexto(observacoes),
                CriadoEm = criadoEm,
                AlteradoEm = criadoEm
            };

            item.RecalcularStatus(dataEntrada);
            item.ChavePesquisa = item.MontarChavePesquisa(produto, variacao);
            return item;
        }

        public Quantidade RegistrarReposicao(
            Quantidade quantidadeAdicional,
            DateTime dataReposicao,
            string? variacaoDescricao,
            string? cor,
            string? tamanho,
            string? observacoes,
            Dimensoes? dimensoesReais,
            Validade? validade,
            Dinheiro? novoCustoUnitario,
            Dinheiro? novoPrecoVendaSugerido,
            DateTime alteradoEm)
        {
            QuantidadeAtual = QuantidadeAtual.Add(quantidadeAdicional);
            VariacaoDescricao = NormalizarTexto(variacaoDescricao) ?? VariacaoDescricao;
            Cor = NormalizarTexto(cor) ?? Cor;
            Tamanho = NormalizarTexto(tamanho) ?? Tamanho;
            Observacoes = NormalizarTexto(observacoes) ?? Observacoes;
            DimensoesReais = dimensoesReais ?? DimensoesReais;
            ValidadeEm = validade ?? ValidadeEm;
            UltimaMovimentacaoEm = dataReposicao;
            AlteradoEm = alteradoEm;

            if (novoCustoUnitario is not null) CustoUnitario = novoCustoUnitario;
            if (novoPrecoVendaSugerido is not null) PrecoVendaSugerido = novoPrecoVendaSugerido;

            RecalcularStatus(dataReposicao);
            return QuantidadeAtual;
        }

        public Quantidade RegistrarSaida(Quantidade quantidadeSaida, DateTime dataSaida, DateTime alteradoEm)
        {
            GarantirDisponivelParaSaida(dataSaida);

            if (QuantidadeAtual.Value < quantidadeSaida.Value)
                throw new EstoqueInsuficienteException(ProdutoId, quantidadeSaida.Value, QuantidadeAtual.Value);

            QuantidadeAtual = QuantidadeAtual.Subtract(quantidadeSaida);
            UltimaMovimentacaoEm = dataSaida;
            AlteradoEm = alteradoEm;
            Status = QuantidadeAtual.Value == 0 ? StatusItemEstoque.Esgotado : StatusItemEstoque.Ativo;

            return QuantidadeAtual;
        }

        public void GarantirDisponivelParaSaida(DateTime dataReferencia)
        {
            if (Status == StatusItemEstoque.Bloqueado)
                throw new ItemEstoqueBloqueadoException(Id);

            if (ValidadeEm?.EstaVencido(dataReferencia) == true)
                throw new ItemEstoqueVencidoException(Id, ValidadeEm.DataValidade);

            if (Status == StatusItemEstoque.Descartado)
                throw new RegraDeDominioVioladaException($"Operacao invalida: item de estoque '{Id}' foi descartado.");

            if (QuantidadeAtual.Value <= 0)
                throw new EstoqueInsuficienteException(ProdutoId, 1, QuantidadeAtual.Value);
        }

        public void RecalcularStatus(DateTime dataReferencia)
        {
            if (ValidadeEm?.EstaVencido(dataReferencia) == true)
            {
                Status = StatusItemEstoque.Vencido;
                return;
            }

            if (Status == StatusItemEstoque.Bloqueado || Status == StatusItemEstoque.Descartado)
                return;

            Status = QuantidadeAtual.Value == 0 ? StatusItemEstoque.Esgotado : StatusItemEstoque.Ativo;
        }

        public string MontarChavePesquisa(Produto produto, ProdutoVariacao? variacao)
        {
            var partes = new[]
            {
                CodigoInterno,
                variacao?.Sku?.Value,
                produto.SkuBase?.Value,
                CodigoMarketplace,
                CodigoLote?.Value,
                produto.Nome,
                variacao?.Nome,
                variacao?.Cor,
                variacao?.Tamanho,
                produto.CodigoBarras,
                variacao?.CodigoBarras
            };

            return string.Join(" ", partes.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()));
        }

        private static string? NormalizarTexto(string? valor) =>
            string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
    }
}
