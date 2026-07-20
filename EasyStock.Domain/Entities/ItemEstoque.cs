using EasyStock.Domain.Defaults;
using EasyStock.Domain.ValueObjects;

namespace EasyStock.Domain.Entities
{
    public class ItemEstoque
    {
        public Guid Id { get; set; }
        public Guid EmpresaId { get; set; }
        public Guid ProdutoId { get; set; }
        public Guid? ProdutoVariacaoId { get; set; }
        public Guid? LojaId { get; set; }

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
        public Guid? FornecedorId { get; set; }

        public Quantidade QuantidadeInicial { get; set; } = null!;
        public Quantidade QuantidadeAtual { get; set; } = null!;

        /// <summary>
        /// Saldo a repor (descoberto). Quando uma saída excede o disponível, o saldo vai a 0
        /// e a falta fica registrada aqui — sinal auditável de "repor de verdade", sem
        /// fabricar entrada-fantasma (#540). Sempre &gt;= 0.
        /// </summary>
        public Quantidade QuantidadeDescoberta { get; set; } = Quantidade.Zero;
        public int QuantidadeMinima { get; set; } = OperacionalDefaults.QuantidadeMinima;
        public int QuantidadeCritica { get; set; } = OperacionalDefaults.QuantidadeCritica;
        public decimal VelocidadeSaidaDiaria { get; set; }
        public int DiasSemMovimentacao { get; set; }
        public int? PrevisaoZeramentoDias { get; set; }

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
        public ICollection<ItemVenda> ItensVenda { get; set; } = new List<ItemVenda>();
        public ICollection<MovimentacaoEstoque> Movimentacoes { get; set; } = new List<MovimentacaoEstoque>();
        public Loja? Loja { get; set; }
        public Fornecedor? Fornecedor { get; set; }

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
                QuantidadeMinima = OperacionalDefaults.QuantidadeMinima,
                QuantidadeCritica = OperacionalDefaults.QuantidadeCritica,
                VelocidadeSaidaDiaria = 0m,
                DiasSemMovimentacao = 0,
                PrevisaoZeramentoDias = null,
                CustoUnitario = custoUnitario,
                PrecoVendaSugerido = precoVendaSugerido ?? produto.PrecoReferencia,
                EntradaEm = dataEntrada,
                ValidadeEm = validade,
                UltimaMovimentacaoEm = dataEntrada,
                Status = StatusItemEstoque.Ok,
                Observacoes = NormalizarTexto(observacoes),
                CriadoEm = criadoEm,
                AlteradoEm = criadoEm
            };

            item.RecalcularIndicadores(dataEntrada);
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

            RecalcularIndicadores(dataReposicao);
            return QuantidadeAtual;
        }

        public Quantidade RegistrarSaida(Quantidade quantidadeSaida, DateTime dataSaida, DateTime alteradoEm, bool permitirVencido = false)
        {
            GarantirDisponivelParaSaida(dataSaida, permitirVencido);

            if (QuantidadeAtual.Value < quantidadeSaida.Value)
                throw new EstoqueInsuficienteException(ProdutoId, quantidadeSaida.Value, QuantidadeAtual.Value);

            QuantidadeAtual = QuantidadeAtual.Subtract(quantidadeSaida);
            UltimaMovimentacaoEm = dataSaida;
            AlteradoEm = alteradoEm;
            RecalcularIndicadores(dataSaida);

            return QuantidadeAtual;
        }

        public void GarantirDisponivelParaSaida(DateTime dataReferencia, bool permitirVencido = false)
        {
            GarantirOperavelParaSaida(dataReferencia, permitirVencido);

            if (QuantidadeAtual.Value <= 0)
                throw new EstoqueInsuficienteException(ProdutoId, 1, QuantidadeAtual.Value);
        }

        /// <summary>
        /// Guards de operabilidade que NÃO dependem de saldo (Bloqueado/Vencido/Descartado).
        /// Continuam valendo mesmo na saída com descoberto. <paramref name="permitirVencido"/>
        /// (#983) fura APENAS o guard de vencido — usado por naturezas de baixa/descarte
        /// (<see cref="Enums.NaturezaMovimentacaoEstoqueExtensions.PermiteBaixaDeLoteVencido"/>);
        /// Bloqueado/Descartado seguem barrando incondicionalmente.
        /// </summary>
        private void GarantirOperavelParaSaida(DateTime dataReferencia, bool permitirVencido = false)
        {
            if (Status == StatusItemEstoque.Bloqueado)
                throw new ItemEstoqueBloqueadoException(Id);

            if (!permitirVencido && ValidadeEm?.EstaVencido(OperacionalFuso.DataOperacional(dataReferencia)) == true)
                throw new ItemEstoqueVencidoException(Id, ValidadeEm.DataValidade);

            if (Status == StatusItemEstoque.Descartado)
                throw new RegraDeDominioVioladaException($"Operacao invalida: item de estoque '{Id}' foi descartado.");
        }

        /// <summary>
        /// Saída que permite descoberto (#540, decisão Felipe): a operação não trava quando a
        /// quantidade solicitada excede o disponível. O saldo vai a 0 e a falta é somada a
        /// <see cref="QuantidadeDescoberta"/>, auditável, para reposição posterior — sem fabricar
        /// entrada-fantasma. Bloqueado/Descartado continuam barrando; Vencido barra a menos que
        /// <paramref name="permitirVencido"/> seja true (#983).
        /// </summary>
        public Quantidade RegistrarSaidaPermitindoDescoberto(Quantidade quantidadeSaida, DateTime dataSaida, DateTime alteradoEm, bool permitirVencido = false)
        {
            GarantirOperavelParaSaida(dataSaida, permitirVencido);

            var disponivel = QuantidadeAtual.Value;
            if (quantidadeSaida.Value <= disponivel)
            {
                QuantidadeAtual = QuantidadeAtual.Subtract(quantidadeSaida);
            }
            else
            {
                var falta = quantidadeSaida.Value - disponivel;
                QuantidadeDescoberta = QuantidadeDescoberta.Add(Quantidade.From(falta));
                QuantidadeAtual = Quantidade.Zero;
            }

            UltimaMovimentacaoEm = dataSaida;
            AlteradoEm = alteradoEm;
            RecalcularIndicadores(dataSaida);

            return QuantidadeAtual;
        }

        public void RestaurarQuantidade(Quantidade quantidade, DateTime alteradoEm)
        {
            QuantidadeAtual = QuantidadeAtual.Add(quantidade);
            UltimaMovimentacaoEm = alteradoEm;
            AlteradoEm = alteradoEm;
            RecalcularIndicadores(alteradoEm);
        }

        /// <summary>
        /// Aplica um ajuste de contagem fisica (ADR Inventario): muta a quantidade por
        /// DELTA relativo ao saldo ATUAL, nao por valor absoluto. delta = contado -
        /// esperadoNoMomento (a divergencia achada no instante da contagem). Aplicar
        /// relativo ao atual preserva vendas concorrentes ocorridas entre contar e aplicar.
        /// Se o resultado ficar negativo (oversell concorrente), pisa em 0 e registra o
        /// excedente em <see cref="QuantidadeDescoberta"/> (#540). A contagem fisica e a
        /// verdade: a QuantidadeDescoberta previa e zerada antes de aplicar. NAO barra por
        /// status (reconciliacao, nao saida); Vencido permanece Vencido no recalc.
        /// </summary>
        public Quantidade AplicarAjusteContagem(
            Quantidade qtdContada,
            Quantidade qtdEsperadaNoMomento,
            DateTime dataAjuste,
            DateTime alteradoEm)
        {
            var delta = qtdContada.Value - qtdEsperadaNoMomento.Value;

            // A contagem reconcilia a realidade; a divida sintetica de oversell anterior se resolve.
            QuantidadeDescoberta = Quantidade.Zero;

            var alvo = QuantidadeAtual.Value + delta;
            if (alvo >= 0)
            {
                QuantidadeAtual = Quantidade.From(alvo);
            }
            else
            {
                QuantidadeDescoberta = Quantidade.From(-alvo);
                QuantidadeAtual = Quantidade.Zero;
            }

            UltimaMovimentacaoEm = dataAjuste;
            AlteradoEm = alteradoEm;
            RecalcularIndicadores(dataAjuste);
            return QuantidadeAtual;
        }

        public void AtualizarVelocidadeSaida(decimal velocidadeSaidaDiaria, DateTime dataReferencia)
        {
            VelocidadeSaidaDiaria = Math.Max(0m, decimal.Round(velocidadeSaidaDiaria, 2));
            RecalcularIndicadores(dataReferencia);
        }

        public void RecalcularStatus(DateTime dataReferencia) =>
            RecalcularIndicadores(dataReferencia);

        public void RecalcularIndicadores(DateTime dataReferencia, int diasAlertaParado = OperacionalDefaults.DiasAlertaParado)
        {
            // Converte o instante UTC para o dia operacional de Brasilia (ADR-0032).
            // Assinatura externa mantem DateTime para nao quebrar callers; conversao e interna.
            var dataOp = OperacionalFuso.DataOperacional(dataReferencia);
            if (ValidadeEm?.EstaVencido(dataOp) == true)
            {
                Status = StatusItemEstoque.Vencido;
                AtualizarDiasSemMovimentacao(dataReferencia);
                AtualizarPrevisao();
                return;
            }

            if (Status == StatusItemEstoque.Bloqueado || Status == StatusItemEstoque.Descartado)
            {
                AtualizarDiasSemMovimentacao(dataReferencia);
                AtualizarPrevisao();
                return;
            }

            AtualizarDiasSemMovimentacao(dataReferencia);
            AtualizarPrevisao();

            if (QuantidadeAtual.Value == 0)
            {
                Status = StatusItemEstoque.Esgotado;
                return;
            }

            if (QuantidadeAtual.Value <= QuantidadeCritica)
            {
                Status = StatusItemEstoque.Critical;
                return;
            }

            if (QuantidadeAtual.Value < QuantidadeMinima)
            {
                Status = StatusItemEstoque.Warn;
                return;
            }

            if (DiasSemMovimentacao >= diasAlertaParado)
            {
                Status = StatusItemEstoque.Slow;
                return;
            }

            Status = StatusItemEstoque.Ok;
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

        private void AtualizarDiasSemMovimentacao(DateTime dataReferencia)
        {
            var baseDate = (UltimaMovimentacaoEm ?? EntradaEm).Date;
            DiasSemMovimentacao = Math.Max(0, (dataReferencia.Date - baseDate).Days);
        }

        private void AtualizarPrevisao()
        {
            PrevisaoZeramentoDias = VelocidadeSaidaDiaria <= 0m
                ? null
                : (int?)Math.Floor(QuantidadeAtual.Value / VelocidadeSaidaDiaria);
        }
    }
}
