using EasyStock.Application.Ports.Output.Persistence.Pagamentos;

namespace EasyStock.Application.UseCases.Pedidos.Cobranca;

public sealed record RegistrarEstornoManualInput(Guid EmpresaId, Guid PedidoId, Guid OperacaoId, Guid PagamentoId,
    decimal Valor, string Metodo, string Motivo, string Referencia, bool ValorJaDevolvido,
    Guid UsuarioId, string? UsuarioNome, NivelAcesso NivelSolicitante);

public sealed class RegistrarEstornoManualUseCase(IPedidoRepository pedidos, ICobrancaPedidoRepository cobrancas,
    ICaixaRepository caixa, IUnitOfWork uow, TimeProvider relogio)
{
    public Task<PedidoEstornoManual> ExecuteAsync(RegistrarEstornoManualInput input, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(input.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(input.PedidoId, "PedidoId");
        UseCaseGuards.EnsureNotEmpty(input.PagamentoId, "PagamentoId");
        UseCaseGuards.EnsureNotEmpty(input.OperacaoId, "OperacaoId");
        UseCaseGuards.EnsureNotEmpty(input.UsuarioId, "UsuarioId");
        if (input.NivelSolicitante is not (NivelAcesso.Gerente or NivelAcesso.Admin or NivelAcesso.SuperAdmin))
            throw new UnauthorizedAccessException("Só a dona ou um gerente pode registrar uma devolução.");
        if (!input.ValorJaDevolvido)
            throw new UseCaseValidationException("Confirme que o valor já foi devolvido ao cliente.");
        if (input.Valor <= 0 || input.Valor != decimal.Round(input.Valor, 2))
            throw new UseCaseValidationException("Informe um valor positivo com até duas casas decimais.");
        if (input.Metodo is not ("pix" or "dinheiro" or "credito" or "debito" or "transferencia" or "outro"))
            throw new UseCaseValidationException("Informe o meio usado para devolver o valor.");
        if (string.IsNullOrWhiteSpace(input.Motivo) || input.Motivo.Length > 500)
            throw new UseCaseValidationException("Informe o motivo com até 500 caracteres.");
        if (string.IsNullOrWhiteSpace(input.Referencia) || input.Referencia.Length > 120)
            throw new UseCaseValidationException("Informe o comprovante ou referência com até 120 caracteres.");
        if (input.UsuarioNome?.Length > 120)
            throw new UseCaseValidationException("Nome do responsável excede 120 caracteres.");

        return uow.ExecuteInTransactionSemRetryAsync(async token =>
        {
            // Recebimento, remoção e devolução compartilham o lock do pedido.
            await pedidos.TravarAsync(input.EmpresaId, input.PedidoId, token);
            var pedido = await pedidos.GetByIdWithDetailsAsync(input.EmpresaId, input.PedidoId);
            if (pedido is null || pedido.EmpresaId != input.EmpresaId)
                throw new CobrancaPedidoNaoEncontradoException(input.PedidoId);
            var estornos = await pedidos.ListarEstornosManuaisAsync(input.EmpresaId, input.PedidoId, token);
            var anterior = estornos.FirstOrDefault(e => e.Id == input.OperacaoId);
            if (anterior is not null)
            {
                if (anterior.PagamentoId != input.PagamentoId || anterior.Valor != input.Valor
                    || anterior.Metodo != input.Metodo || anterior.Motivo != input.Motivo.Trim() || anterior.Referencia != input.Referencia.Trim())
                    throw new CobrancaPedidoConflitoException("operacao_reutilizada", "Esta confirmação já foi usada com outros dados.");
                return anterior;
            }
            var pagamento = pedido.Pagamentos.FirstOrDefault(p => p.Id == input.PagamentoId);
            if (pagamento is null)
                throw new CobrancaPedidoConflitoException("sem_pagamento", "Recebimento não encontrado neste pedido.");
            var lista = await cobrancas.ListarDoPedidoAsync(input.EmpresaId, input.PedidoId, token);
            if (!ConsultarEstornosManuaisUseCase.EhManual(pagamento, lista))
                throw new CobrancaPedidoConflitoException("estorno_online", "Este recebimento exige estorno pelo Mercado Pago.");
            var disponivel = pagamento.Valor - estornos.Where(e => e.PagamentoId == pagamento.Id).Sum(e => e.Valor);
            if (input.Valor > disponivel)
                throw new CobrancaPedidoConflitoException("valor_ja_devolvido", "O valor excede o saldo ainda não devolvido deste recebimento. Atualize a ficha.");
            var agora = relogio.GetUtcNow().UtcDateTime;
            if (await caixa.GetFechamentoDoDiaAsync(input.EmpresaId, HorarioBrasil.DataOperacional(agora), pedido.LojaId) is not null)
                throw new UseCaseValidationException("O Caixa de hoje já foi fechado. A devolução ainda não foi registrada.");

            var estorno = new PedidoEstornoManual
            {
                Id = input.OperacaoId, EmpresaId = input.EmpresaId, PedidoId = pedido.Id, PagamentoId = pagamento.Id,
                Valor = input.Valor, Metodo = input.Metodo, Motivo = input.Motivo.Trim(), Referencia = input.Referencia.Trim(),
                UsuarioId = input.UsuarioId, UsuarioNome = input.UsuarioNome, RegistradoEm = agora
            };
            var movimento = MovimentoCaixa.Criar(input.EmpresaId, "saida", estorno.Valor, agora, pedido.LojaId);
            movimento.Id = estorno.Id;
            movimento.CriadoEm = agora;
            movimento.Metodo = estorno.Metodo;
            movimento.Categoria = "Devolução de pedido";
            movimento.Descricao = $"Devolução do pedido {pedido.Id.ToString("N")[..8].ToUpperInvariant()}: {estorno.Motivo}";
            movimento.Referencia = estorno.Referencia;
            movimento.RegistradoPorUserId = estorno.UsuarioId;
            movimento.RegistradoPorNome = estorno.UsuarioNome;
            movimento.Origem = PedidoEstornoManual.OrigemCaixa;
            await caixa.AddMovimentoAsync(movimento);
            await pedidos.AdicionarEstornoManualAsync(estorno);
            await pedidos.AddEventoAsync(new PedidoEvento
            {
                Id = Guid.NewGuid(), PedidoId = pedido.Id, Tipo = "devolucao_manual", OcorridoEm = agora,
                UsuarioId = estorno.UsuarioId, UsuarioNome = estorno.UsuarioNome, Origem = "web",
                Detalhes = $"Devolução confirmada: {estorno.Valor.ToString("C", Cultura.PtBr)} ({estorno.Metodo}). Motivo: {estorno.Motivo}. Referência: {estorno.Referencia}. Operação: {estorno.Id}"
            });
            await uow.CommitAsync();
            return estorno;
        }, ct);
    }
}
