namespace EasyStock.Admin.Pages.Notificacoes;

/// <summary>
/// Catalogo: para cada TipoEventoNotificacao, lista as variaveis Scriban que
/// o disparador injeta no contexto. Usado pela UI do editor de Template para
/// (a) sugerir chips clicaveis com auto-insert e (b) validar uso de variaveis
/// desconhecidas. Fonte da verdade movel — quando um disparador novo for criado,
/// atualizar aqui. O backend nao consome este arquivo; e so para guiar a UX
/// e popular o preview com valores de exemplo coerentes.
/// </summary>
public static class NotificacoesVariaveisCatalogo
{
    public record Variavel(string Nome, string Descricao, string Exemplo);

    public static IReadOnlyDictionary<string, IReadOnlyList<Variavel>> PorEvento { get; } =
        new Dictionary<string, IReadOnlyList<Variavel>>(StringComparer.OrdinalIgnoreCase)
        {
            ["ProdutoVencendo"] = new[]
            {
                new Variavel("produto_nome", "Nome do produto", "Leite Integral 1L"),
                new Variavel("dias_restantes", "Dias ate o vencimento", "3"),
                new Variavel("data_vencimento", "Data ISO do vencimento", "2026-05-14"),
                new Variavel("lote_numero", "Numero do lote", "L20260512"),
                new Variavel("quantidade", "Quantidade em estoque", "12"),
            },
            ["ProdutoVencido"] = new[]
            {
                new Variavel("produto_nome", "Nome do produto", "Iogurte 200g"),
                new Variavel("data_vencimento", "Data ISO do vencimento", "2026-05-08"),
                new Variavel("lote_numero", "Numero do lote", "L20260501"),
                new Variavel("quantidade", "Quantidade vencida", "5"),
            },
            ["TarefaPendente"] = new[]
            {
                new Variavel("tarefa_titulo", "Titulo da tarefa", "Inventario semanal"),
                new Variavel("prazo", "Prazo da tarefa", "2026-05-12"),
                new Variavel("responsavel_nome", "Nome do responsavel", "Maria Silva"),
            },
            ["ResetSenha"] = new[]
            {
                new Variavel("nome", "Nome do usuario", "Joao"),
                new Variavel("link_redefinicao", "URL de redefinicao", "https://app.easystok.com/reset?token=..."),
                new Variavel("expira_em_minutos", "Minutos ate o link expirar", "60"),
            },
            ["AssinaturaExpirando"] = new[]
            {
                new Variavel("nome", "Nome do usuario", "Joao"),
                new Variavel("vencimento", "Data de vencimento", "2026-05-18"),
                new Variavel("valor", "Valor a pagar", "R$ 99,90"),
                new Variavel("qr_code", "QR code base64 (sem prefixo)", "iVBORw0KGgo..."),
                new Variavel("pix_copia_cola", "Pix copia-e-cola", "00020126..."),
            },
            ["AssinaturaExpirada"] = new[]
            {
                new Variavel("nome", "Nome do usuario", "Joao"),
                new Variavel("urgencia", "Mensagem de urgencia", "Seu acesso sera suspenso em 24h"),
                new Variavel("valor", "Valor a pagar", "R$ 99,90"),
                new Variavel("numero_lembrete", "Numero do lembrete (1, 2 ou 3)", "2"),
                new Variavel("qr_code", "QR code base64", "iVBORw0KGgo..."),
                new Variavel("pix_copia_cola", "Pix copia-e-cola", "00020126..."),
            },
            ["BroadcastSuperAdmin"] = new[]
            {
                new Variavel("titulo", "Titulo do broadcast", "Manutencao programada"),
                new Variavel("mensagem", "Corpo da mensagem", "O sistema ficara indisponivel..."),
                new Variavel("nome", "Nome do destinatario", "Joao"),
            },
            ["ConfirmacaoEmail"] = new[]
            {
                new Variavel("nome", "Nome do usuario", "Joao"),
                new Variavel("link_confirmacao", "URL de confirmacao", "https://app.easystok.com/confirm?token=..."),
                new Variavel("expira_em_horas", "Horas ate o link expirar", "24"),
            },
            ["AlertaEstoqueCritico"] = new[]
            {
                new Variavel("produto_nome", "Nome do produto", "Pao Frances"),
                new Variavel("quantidade_atual", "Quantidade atual", "3"),
                new Variavel("quantidade_minima", "Estoque minimo", "20"),
                new Variavel("unidade", "Unidade de medida", "kg"),
            },
            ["TicketCriado"] = new[]
            {
                new Variavel("titulo", "Titulo do ticket", "Erro ao emitir NFC-e"),
                new Variavel("empresaNome", "Nome da empresa", "Padaria do Joao"),
                new Variavel("prioridade", "Prioridade", "Alta"),
                new Variavel("nivel", "Nivel de atendimento", "N1"),
                new Variavel("ticketId", "ID do ticket", "TK-12345"),
            },
            ["TicketRespondidoCliente"] = new[]
            {
                new Variavel("titulo", "Titulo do ticket", "Erro ao emitir NFC-e"),
                new Variavel("empresaNome", "Nome da empresa", "Padaria do Joao"),
                new Variavel("clienteNome", "Nome do cliente", "Maria Silva"),
            },
            ["TicketRespondidoAdmin"] = new[]
            {
                new Variavel("titulo", "Titulo do ticket", "Erro ao emitir NFC-e"),
                new Variavel("atendenteNome", "Nome do atendente", "Suporte EasyStok"),
            },
            ["TicketStatusAlterado"] = new[]
            {
                new Variavel("titulo", "Titulo do ticket", "Erro ao emitir NFC-e"),
                new Variavel("statusAntes", "Status anterior", "Aberto"),
                new Variavel("statusDepois", "Novo status", "Em atendimento"),
            },
            ["TicketAtribuido"] = new[]
            {
                new Variavel("titulo", "Titulo do ticket", "Erro ao emitir NFC-e"),
                new Variavel("atendenteNome", "Nome do atendente designado", "Suporte EasyStok"),
            },
            ["TicketEncaminhadoNivel"] = new[]
            {
                new Variavel("titulo", "Titulo do ticket", "Erro ao emitir NFC-e"),
                new Variavel("nivelOrigem", "Nivel de origem", "N1"),
                new Variavel("nivelDestino", "Nivel de destino", "N2"),
                new Variavel("motivo", "Motivo do encaminhamento", "Requer analise tecnica"),
            },
            ["SlaProximoVencer"] = new[]
            {
                new Variavel("titulo", "Titulo do ticket", "Erro ao emitir NFC-e"),
                new Variavel("percentual", "Percentual do SLA consumido", "85"),
                new Variavel("minutosRestantes", "Minutos restantes", "32"),
                new Variavel("tipoSla", "Tipo de SLA (FirstResponse/Resolucao)", "FirstResponse"),
            },
            ["SlaViolado"] = new[]
            {
                new Variavel("titulo", "Titulo do ticket", "Erro ao emitir NFC-e"),
                new Variavel("tipoSla", "Tipo de SLA violado", "FirstResponse"),
                new Variavel("empresaNome", "Nome da empresa", "Padaria do Joao"),
                new Variavel("prioridade", "Prioridade", "Alta"),
                new Variavel("nivel", "Nivel de atendimento", "N1"),
            },
            ["BugFixCriado"] = new[]
            {
                new Variavel("titulo", "Titulo do bug-fix", "Corrigir validacao de CPF"),
                new Variavel("severidade", "Severidade", "Alta"),
                new Variavel("componente", "Componente afetado", "checkout"),
                new Variavel("ticketOrigemId", "ID do ticket original", "TK-12345"),
            },
            ["FaturaCriada"] = new[]
            {
                new Variavel("nome", "Nome do cliente", "Joao"),
                new Variavel("numero_fatura", "Numero da fatura", "FAT-2026-05-001"),
                new Variavel("valor", "Valor formatado", "R$ 199,90"),
                new Variavel("vencimento", "Data de vencimento", "2026-05-20"),
                new Variavel("link_pagamento", "URL para pagar", "https://app.easystok.com/faturas/..."),
            },
            ["FaturaVencendo"] = new[]
            {
                new Variavel("nome", "Nome do cliente", "Joao"),
                new Variavel("numero_fatura", "Numero da fatura", "FAT-2026-05-001"),
                new Variavel("valor", "Valor", "R$ 199,90"),
                new Variavel("dias_restantes", "Dias ate vencer", "3"),
                new Variavel("vencimento", "Data de vencimento", "2026-05-14"),
                new Variavel("link_pagamento", "URL para pagar", "https://app.easystok.com/faturas/..."),
            },
            ["FaturaPaga"] = new[]
            {
                new Variavel("nome", "Nome do cliente", "Joao"),
                new Variavel("numero_fatura", "Numero da fatura", "FAT-2026-05-001"),
                new Variavel("valor", "Valor pago", "R$ 199,90"),
                new Variavel("data_pagamento", "Data do pagamento", "2026-05-11"),
                new Variavel("link_recibo", "URL do recibo/NFS-e", "https://app.easystok.com/recibos/..."),
            },
            ["FaturaVencida"] = new[]
            {
                new Variavel("nome", "Nome do cliente", "Joao"),
                new Variavel("numero_fatura", "Numero da fatura", "FAT-2026-05-001"),
                new Variavel("valor", "Valor em atraso", "R$ 199,90"),
                new Variavel("dias_em_atraso", "Dias em atraso", "5"),
                new Variavel("link_pagamento", "URL para regularizar", "https://app.easystok.com/faturas/..."),
            },
            ["PagamentoConfirmado"] = new[]
            {
                new Variavel("nome", "Nome do cliente", "Joao"),
                new Variavel("valor", "Valor pago", "R$ 199,90"),
                new Variavel("metodo", "Metodo de pagamento", "Pix"),
                new Variavel("data_pagamento", "Data do pagamento", "2026-05-11"),
            },
            ["PagamentoFalhou"] = new[]
            {
                new Variavel("nome", "Nome do cliente", "Joao"),
                new Variavel("valor", "Valor tentado", "R$ 199,90"),
                new Variavel("metodo", "Metodo de pagamento", "Cartao"),
                new Variavel("motivo", "Motivo da falha", "Saldo insuficiente"),
                new Variavel("link_retry", "URL para tentar novamente", "https://app.easystok.com/pagar/..."),
            },
            ["ConviteCsat"] = new[]
            {
                new Variavel("nome", "Nome do cliente", "Maria"),
                new Variavel("titulo_ticket", "Titulo do ticket fechado", "Erro ao emitir NFC-e"),
                new Variavel("link_pesquisa", "URL da pesquisa CSAT", "https://app.easystok.com/csat?token=..."),
                new Variavel("atendenteNome", "Atendente que resolveu", "Suporte EasyStok"),
            },
        };

    public static IReadOnlyList<Variavel> ParaEvento(string? tipoEvento)
    {
        if (string.IsNullOrWhiteSpace(tipoEvento)) return Array.Empty<Variavel>();
        return PorEvento.TryGetValue(tipoEvento, out var lista) ? lista : Array.Empty<Variavel>();
    }
}
