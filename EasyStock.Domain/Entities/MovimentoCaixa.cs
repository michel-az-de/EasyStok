namespace EasyStock.Domain.Entities
{
    /// <summary>
    /// Movimento de caixa (Onda P3) — entradas/saídas além das vendas.
    ///
    /// Tipos:
    ///  - <c>abertura</c>: sangria de caixa pra começar o dia (saldo inicial)
    ///  - <c>fechamento</c>: marca caixa fechado (snapshot vai em <see cref="FechamentoCaixa"/>)
    ///  - <c>entrada</c>: recebimento extra (não vinculado a venda) — ex: troco de fornecedor
    ///  - <c>saida</c>: despesa, sangria, pagamento de fornecedor, etc
    ///
    /// Valor é sempre positivo (o sinal vem do <see cref="Tipo"/>).
    /// Pagamentos de pedido NÃO viram MovimentoCaixa — ficam em
    /// <see cref="PedidoPagamento"/> e o resumo do dia agrega ambos.
    /// </summary>
    public class MovimentoCaixa
    {
        public Guid Id { get; set; }
        public Guid EmpresaId { get; set; }
        public Guid? LojaId { get; set; }

        /// <summary>"abertura" | "fechamento" | "entrada" | "saida".</summary>
        public string Tipo { get; set; } = "entrada";

        public decimal Valor { get; set; }
        public string? Descricao { get; set; }

        /// <summary>"pix" | "dinheiro" | "credito" | "debito" | "transferencia" | "outro".</summary>
        public string? Metodo { get; set; }

        /// <summary>Categoria livre pra classificar despesas (ex: "embalagem", "matéria-prima").</summary>
        public string? Categoria { get; set; }

        /// <summary>Identificador externo (txid PIX, NSU cartão, n° NF, etc).</summary>
        public string? Referencia { get; set; }

        /// <summary>Data efetiva do movimento (pode ser retroativo — ex: lançar despesa de ontem).</summary>
        public DateTime DataMovimento { get; set; }

        public Guid? RegistradoPorUserId { get; set; }
        public string? RegistradoPorNome { get; set; }
        public string? Origem { get; set; } // "web" | "mobile" | "api"

        /// <summary>Estornado por quê? Mantém movimento mas marca como cancelado.</summary>
        public DateTime? EstornadoEm { get; set; }
        public Guid? EstornadoPorUserId { get; set; }
        public string? EstornadoPorNome { get; set; }
        public string? MotivoEstorno { get; set; }

        public DateTime CriadoEm { get; set; }

        /// <summary>Quando este movimento de "abertura" já foi notificado como caixa esquecido
        /// aberto (dedup do CaixaEsquecidoJob — 1 aviso por sessão, não 1/dia). Null = ainda não
        /// notificado. Só faz sentido para Tipo=="abertura".</summary>
        public DateTime? NotificadoEsquecidoEm { get; set; }

        /// <summary>#2084 (B1): abertura feita sozinha pelo primeiro pagamento em dinheiro, com a gaveta herdada do
        /// último fechamento, que a gerente ainda não conferiu. A abertura manual do dia confirma (ver
        /// <see cref="ConfirmarAbertura"/>). Só faz sentido para Tipo=="abertura".</summary>
        public bool ConfirmacaoPendente { get; set; }

        public Empresa? Empresa { get; set; }
        public Loja? Loja { get; set; }

        /// <summary>True se efetivo (não estornado).</summary>
        public bool Ativo => EstornadoEm == null;

        public static MovimentoCaixa Criar(
            Guid empresaId, string tipo, decimal valor,
            DateTime? dataMovimento = null, Guid? lojaId = null)
        {
            var agora = DateTime.UtcNow;
            return new MovimentoCaixa
            {
                Id = Guid.NewGuid(),
                EmpresaId = empresaId,
                LojaId = lojaId,
                Tipo = tipo,
                Valor = Math.Abs(valor),
                DataMovimento = dataMovimento ?? agora,
                CriadoEm = agora
            };
        }

        public void Estornar(Guid? userId, string? userNome, string? motivo)
        {
            EstornadoEm = DateTime.UtcNow;
            EstornadoPorUserId = userId;
            EstornadoPorNome = userNome;
            MotivoEstorno = motivo;
        }

        /// <summary>#2084 (B1, A1): a gerente conferiu a gaveta da abertura automática. <paramref name="saldoInicial"/> é
        /// o troco que a gaveta tinha ao abrir, já descontado o dinheiro que entrou depois (quem chama calcula). A
        /// descrição guarda o valor herdado e o confirmado; quem confirmou vira quem registrou.</summary>
        public void ConfirmarAbertura(decimal saldoInicial, Guid? userId, string? userNome, string? observacao)
        {
            var herdado = Valor;
            Valor = Math.Abs(saldoInicial);
            RegistradoPorUserId = userId;
            RegistradoPorNome = userNome ?? RegistradoPorNome;
            var registro = $"Abertura automática conferida: herdado {herdado.ToString("N2", PtBr)}, confirmado {Valor.ToString("N2", PtBr)}.";
            Descricao = string.IsNullOrWhiteSpace(observacao) ? registro : $"{registro} {observacao.Trim()}";
            ConfirmacaoPendente = false;
        }

        private static readonly System.Globalization.CultureInfo PtBr = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");

        /// <summary>Carimba esta abertura como já notificada (caixa esquecido). Idempotente —
        /// preserva o primeiro carimbo. Kind=Utc para a coluna timestamptz.</summary>
        public void MarcarNotificadoEsquecido() => NotificadoEsquecidoEm ??= DateTime.UtcNow;

        /// <summary>Sinal financeiro do movimento (positivo = entra no caixa, negativo = sai).</summary>
        public decimal SinalNoCaixa => Tipo switch
        {
            "abertura" => +Valor,
            "entrada" => +Valor,
            "saida" => -Valor,
            // fechamento é só marcador, não move saldo
            _ => 0m
        };
    }

    /// <summary>
    /// Snapshot consolidado do fechamento de um dia. Calculado uma vez no
    /// momento do fechamento — preserva o resultado mesmo se alguém adicionar
    /// movimento retroativo depois (movimentos retroativos podem mudar
    /// <see cref="MovimentoCaixa"/>, mas o snapshot fica).
    /// </summary>
    public class FechamentoCaixa
    {
        public Guid Id { get; set; }
        public Guid EmpresaId { get; set; }
        public Guid? LojaId { get; set; }

        public DateOnly Data { get; set; }

        public decimal SaldoInicial { get; set; }
        public decimal TotalVendas { get; set; }
        public decimal TotalPagamentosPedidos { get; set; }
        public decimal TotalEntradasExtras { get; set; }
        public decimal TotalSaidasExtras { get; set; }
        public decimal SaldoFinal { get; set; }

        public Guid? FechadoPorUserId { get; set; }
        public string? FechadoPorNome { get; set; }
        public string? Observacoes { get; set; }
        public DateTime FechadoEm { get; set; }

        /// <summary>#2084 (B3): dinheiro que a gerente contou na gaveta ao fechar. Nulo nos fechamentos antigos
        /// e quando ninguém contou.</summary>
        public decimal? ValorContado { get; set; }

        /// <summary>#2084 (D1): dinheiro que o sistema esperava na gaveta no dia deste fechamento (o dia da
        /// abertura, ADR-0034). Nulo nos fechamentos antigos.</summary>
        public decimal? DinheiroEsperado { get; set; }

        /// <summary>#2084 (D1): no fechamento de um caixa esquecido, o dinheiro que entrou HOJE sob a mesma sessão.
        /// Já estava na gaveta contada, mas pertence ao caixa de hoje; a abertura seguinte não o herda.
        /// Zero quando o fechamento é do próprio dia; nulo nos fechamentos antigos.</summary>
        public decimal? DinheiroEntrouDepois { get; set; }

        public Empresa? Empresa { get; set; }
        public Loja? Loja { get; set; }

        public static FechamentoCaixa Criar(
            Guid empresaId, DateOnly data,
            decimal saldoInicial, decimal totalVendas,
            decimal totalPagamentosPedidos, decimal totalEntradasExtras,
            decimal totalSaidasExtras, Guid? lojaId = null)
        {
            var f = new FechamentoCaixa
            {
                Id = Guid.NewGuid(),
                EmpresaId = empresaId,
                LojaId = lojaId,
                Data = data,
                SaldoInicial = saldoInicial,
                TotalVendas = totalVendas,
                TotalPagamentosPedidos = totalPagamentosPedidos,
                TotalEntradasExtras = totalEntradasExtras,
                TotalSaidasExtras = totalSaidasExtras,
                FechadoEm = DateTime.UtcNow
            };
            f.SaldoFinal = saldoInicial + totalVendas + totalPagamentosPedidos
                         + totalEntradasExtras - totalSaidasExtras;
            return f;
        }
    }
}
