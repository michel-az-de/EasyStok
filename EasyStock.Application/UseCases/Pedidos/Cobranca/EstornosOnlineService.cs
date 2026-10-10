using System.Net;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Application.Ports.Output.Persistence.Pagamentos;

namespace EasyStock.Application.UseCases.Pedidos.Cobranca;

public sealed class EstornosOnlineService(IPedidoRepository pedidos, IEstornoOnlineRepository repo,
    ICobrancaPedidoRepository cobrancas, IMercadoPagoClient mp, ICaixaRepository caixa, IUnitOfWork uow,
    ITenantContextAccessor tenant, TimeProvider relogio) : IEstornosOnlineService
{
    private DateTime Agora => relogio.GetUtcNow().UtcDateTime;

    public async Task<EstornosOnlineResult> ConsultarAsync(Guid empresaId, Guid pedidoId, CancellationToken ct = default)
    {
        await ConferirPedidoAsync(empresaId, pedidoId);
        var recebimentos = await repo.RecebimentosAsync(empresaId, pedidoId, ct);
        var estornos = await repo.ListarAsync(empresaId, pedidoId, ct);
        return new(recebimentos.Select(p =>
        {
            var itens = estornos.Where(e => e.PagamentoId == p.PagamentoId).ToList();
            var legado = p.Estornada && itens.Count == 0;
            var devolvido = legado ? p.Valor : itens.Where(e => e.Situacao == PedidoEstornoOnline.Confirmado).Sum(e => e.Valor);
            var reservado = itens.Where(e => e.Situacao == PedidoEstornoOnline.Pendente).Sum(e => e.Valor);
            return new RecebimentoParaEstornoOnline(p.PagamentoId, p.Metodo, p.Valor, p.PagoEm,
                devolvido, reservado, p.Estornada ? 0 : Math.Max(0, p.Valor - devolvido - reservado), legado);
        }).ToList(), estornos);
    }

    public async Task<PedidoEstornoOnline> SolicitarAsync(SolicitarEstornoOnlineInput input, CancellationToken ct = default)
    {
        ExigirGerente(input.NivelSolicitante);
        UseCaseGuards.EnsureNotEmpty(input.OperacaoId, "OperacaoId");
        UseCaseGuards.EnsureNotEmpty(input.PagamentoId, "PagamentoId");
        UseCaseGuards.EnsureNotEmpty(input.UsuarioId, "UsuarioId");
        if (input.Valor <= 0 || input.Valor != decimal.Round(input.Valor, 2))
            throw new UseCaseValidationException("Informe um valor positivo com até duas casas decimais.");
        if (string.IsNullOrWhiteSpace(input.Motivo) || input.Motivo.Length > 500 || input.UsuarioNome?.Length > 120)
            throw new UseCaseValidationException("Informe o motivo com até 500 caracteres e um responsável válido.");

        var operacao = await ComPedidoTravadoAsync(input.EmpresaId, input.PedidoId, async (pagamentos, estornos, token) =>
        {
            var existente = estornos.FirstOrDefault(e => e.Id == input.OperacaoId);
            if (existente is not null)
            {
                if (existente.PagamentoId != input.PagamentoId || existente.Valor != input.Valor || existente.Motivo != input.Motivo.Trim())
                    throw Conflito("operacao_reutilizada", "Esta solicitação já foi usada com outros dados.");
                return existente;
            }
            var pagamento = pagamentos.SingleOrDefault(p => p.PagamentoId == input.PagamentoId)
                ?? throw Conflito("sem_pagamento_online", "Recebimento do Mercado Pago não encontrado neste pedido.");
            if (estornos.Any(e => e.PagamentoId == pagamento.PagamentoId && e.Situacao == PedidoEstornoOnline.Pendente))
                throw Conflito("estorno_pendente", "Confira a solicitação pendente antes de pedir outra devolução.");
            if (pagamento.Estornada || input.Valor > pagamento.Valor - Confirmado(estornos, pagamento))
                throw Conflito("valor_ja_devolvido", "O valor excede o saldo ainda não devolvido deste recebimento.");
            if (await caixa.GetFechamentoDoDiaAsync(input.EmpresaId, HorarioBrasil.DataOperacional(Agora), pagamento.LojaId) is not null)
                throw new UseCaseValidationException("O Caixa de hoje já foi fechado. A devolução não foi solicitada.");
            var nova = new PedidoEstornoOnline
            {
                Id = input.OperacaoId, EmpresaId = input.EmpresaId, PedidoId = input.PedidoId,
                PagamentoId = pagamento.PagamentoId, PagamentoExternoId = pagamento.PagamentoExternoId,
                Valor = input.Valor, Motivo = input.Motivo.Trim(), UsuarioId = input.UsuarioId,
                UsuarioNome = input.UsuarioNome, CriadoEm = Agora
            };
            await repo.AdicionarAsync(nova, token);
            return nova;
        }, ct);
        return operacao.Situacao == PedidoEstornoOnline.Pendente ? await ProcessarAsync(operacao, ct) : operacao;
    }

    public async Task<PedidoEstornoOnline> RetomarAsync(Guid empresaId, Guid pedidoId, Guid operacaoId, NivelAcesso nivel, CancellationToken ct = default)
    {
        ExigirGerente(nivel);
        await ConferirPedidoAsync(empresaId, pedidoId);
        var operacao = (await repo.ListarAsync(empresaId, pedidoId, ct)).SingleOrDefault(e => e.Id == operacaoId)
            ?? throw Conflito("estorno_nao_encontrado", "Solicitação não encontrada neste pedido.");
        return operacao.Situacao == PedidoEstornoOnline.Pendente ? await ProcessarAsync(operacao, ct) : operacao;
    }

    private async Task<PedidoEstornoOnline> ProcessarAsync(PedidoEstornoOnline operacao, CancellationToken ct)
    {
        var primeiroEnvio = false;
        try
        {
            var pagamento = await mp.ConsultarPagamentoAsync(operacao.PagamentoExternoId, ct);
            var recebido = (await repo.RecebimentosAsync(operacao.EmpresaId, operacao.PedidoId, ct))
                .Single(p => p.PagamentoId == operacao.PagamentoId);
            if (!PagamentoConfere(pagamento, recebido, operacao.PedidoId))
                return await DetalharAsync(operacao, "Não foi possível conferir o pagamento no Mercado Pago. Nenhuma nova solicitação foi enviada.", ct);

            // Antes do primeiro POST, incorpora estornos externos/antigos. Após envio sem resposta,
            // um refund sem vínculo pode ser desta própria operação: só o replay da mesma chave resolve.
            if (operacao.EnviadoEm is null)
            {
                var lista = await mp.ListarEstornosAsync(operacao.PagamentoExternoId, ct);
                if (lista.Any(r => !RespostaConfere(r, recebido.PagamentoExternoId) || r.Status is not ("approved" or "rejected" or "cancelled")))
                    return await DetalharAsync(operacao, "Há devolução externa sem confirmação válida. Confira o Mercado Pago antes de retomar.", ct);
                await ImportarAsync(operacao.EmpresaId, operacao.PedidoId, recebido, lista, ct);
                var conhecidos = await repo.ListarAsync(operacao.EmpresaId, operacao.PedidoId, ct);
                if (pagamento!.TransactionAmountRefunded > Confirmado(conhecidos, recebido))
                    return await DetalharAsync(operacao, "O total devolvido no provedor ainda não foi conciliado. Nenhuma nova solicitação foi enviada.", ct);
                if (!pagamento.Aprovado)
                    return await DetalharAsync(operacao, "O pagamento já foi encerrado pelo Mercado Pago e não aceita novo estorno.", ct, true);
            }

            operacao = await ComPedidoTravadoAsync(operacao.EmpresaId, operacao.PedidoId, async (pagamentos, estornos, token) =>
            {
                var atual = estornos.Single(e => e.Id == operacao.Id);
                if (atual.Situacao != PedidoEstornoOnline.Pendente) return atual;
                var p = pagamentos.Single(p => p.PagamentoId == atual.PagamentoId);
                primeiroEnvio = atual.EnviadoEm is null;
                if (primeiroEnvio && (p.Estornada || atual.Valor > p.Valor - Confirmado(estornos, p)))
                {
                    atual.Situacao = PedidoEstornoOnline.Recusado;
                    atual.Detalhe = "O provedor já devolveu este valor. Confira o histórico antes de solicitar outro estorno.";
                }
                else atual.EnviadoEm ??= Agora;
                await repo.AtualizarAsync(atual, token);
                return atual;
            }, ct);
            if (operacao.Situacao != PedidoEstornoOnline.Pendente) return operacao;

            var resposta = operacao.EstornoExternoId is { } id
                ? await mp.ConsultarEstornoAsync(operacao.PagamentoExternoId, id, ct)
                : await mp.EstornarAsync(operacao.PagamentoExternoId, operacao.Valor, operacao.Id.ToString(), ct);
            return await AplicarAsync(operacao, resposta, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or JsonException
            || ex is TaskCanceledException && !ct.IsCancellationRequested)
        {
            // Um 4xx no primeiro POST recusa a operação. Depois de uma resposta perdida, até um
            // erro no replay conserva a reserva: não sabemos se a tentativa anterior foi efetivada.
            var recusado = primeiroEnvio && ex is HttpRequestException { StatusCode: HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity };
            return await DetalharAsync(operacao, recusado
                ? "O Mercado Pago recusou a solicitação. Confira o pagamento e o valor antes de tentar novamente."
                : "Resposta do Mercado Pago indisponível ou indeterminada. Retome esta mesma solicitação; não devolva por outro meio.", ct, recusado);
        }
    }

    private Task<PedidoEstornoOnline> AplicarAsync(PedidoEstornoOnline operacao, EstornoMercadoPagoResult? resposta, CancellationToken ct) =>
        ComPedidoTravadoAsync(operacao.EmpresaId, operacao.PedidoId, async (pagamentos, estornos, token) =>
        {
            var atual = estornos.Single(e => e.Id == operacao.Id);
            if (atual.Situacao != PedidoEstornoOnline.Pendente) return atual;
            if (resposta is null || !RespostaConfere(resposta, atual.PagamentoExternoId) || resposta.Valor != atual.Valor
                || atual.EstornoExternoId is not null && resposta.EstornoId != atual.EstornoExternoId
                || estornos.Any(e => e.Id != atual.Id && e.EstornoExternoId == resposta.EstornoId))
                atual.Detalhe = "Resposta sem confirmação válida do pagamento e valor. Retome a mesma solicitação para conferir.";
            else
            {
                atual.EstornoExternoId = resposta.EstornoId;
                if (resposta.Status == "approved")
                    await ConfirmarAsync(atual, pagamentos.Single(p => p.PagamentoId == atual.PagamentoId), estornos, token);
                else if (resposta.Status is "rejected" or "cancelled")
                {
                    atual.Situacao = PedidoEstornoOnline.Recusado;
                    atual.Detalhe = "Estorno recusado pelo Mercado Pago. Nenhuma saída foi registrada.";
                }
                else atual.Detalhe = "O Mercado Pago ainda não confirmou a devolução. Confira esta solicitação novamente.";
            }
            await repo.AtualizarAsync(atual, token);
            return atual;
        }, ct);

    private Task<PedidoEstornoOnline> DetalharAsync(PedidoEstornoOnline operacao, string detalhe, CancellationToken ct, bool recusado = false) =>
        ComPedidoTravadoAsync(operacao.EmpresaId, operacao.PedidoId, async (_, estornos, token) =>
        {
            var atual = estornos.Single(e => e.Id == operacao.Id);
            if (atual.Situacao != PedidoEstornoOnline.Pendente) return atual;
            atual.Detalhe = detalhe;
            if (recusado) atual.Situacao = PedidoEstornoOnline.Recusado;
            await repo.AtualizarAsync(atual, token);
            return atual;
        }, ct);

    public async Task<bool> SincronizarPagamentoAsync(PagamentoMercadoPago pagamento, CancellationToken ct = default)
    {
        if (!Guid.TryParse(pagamento.ExternalReference, out var pedidoId)) return false;
        var empresaId = await cobrancas.ObterEmpresaIdDoPedidoAsync(pedidoId, ct);
        if (empresaId is null) return false;
        tenant.SetCurrentTenant(empresaId.Value);
        var recebimento = (await repo.RecebimentosAsync(empresaId.Value, pedidoId, ct)).SingleOrDefault(p => p.PagamentoExternoId == pagamento.Id);
        if (recebimento is null) return false;
        if (!PagamentoConfere(pagamento, recebimento, pedidoId))
            throw new InvalidOperationException("Pagamento divergente na conciliação de estornos.");
        var existentes = await repo.ListarAsync(empresaId.Value, pedidoId, ct);
        // O legado já retirou o recebimento do Caixa. Não lançar outra saída retrospectiva.
        if (recebimento.Estornada && !existentes.Any(e => e.PagamentoId == recebimento.PagamentoId)) return false;
        foreach (var pendente in existentes.Where(e => e.PagamentoId == recebimento.PagamentoId && e.Situacao == PedidoEstornoOnline.Pendente))
            await ProcessarAsync(pendente, ct);
        await ImportarAsync(empresaId.Value, pedidoId, recebimento, await mp.ListarEstornosAsync(pagamento.Id, ct), ct);
        if (pagamento.Estornado)
            await ComPedidoTravadoAsync(empresaId.Value, pedidoId, async (pagamentos, estornos, token) =>
            {
                var p = pagamentos.Single(p => p.PagamentoExternoId == pagamento.Id);
                if (estornos.Any(e => e.PagamentoId == p.PagamentoId && e.Situacao == PedidoEstornoOnline.Pendente))
                    throw new InvalidOperationException("A confirmação do estorno ainda está pendente; repetir o webhook.");
                var restante = p.Valor - Confirmado(estornos, p);
                if (restante > 0 && pagamento.Status == PagamentoMercadoPago.ChargedBack)
                {
                    var contestacao = new PedidoEstornoOnline { Id = Guid.NewGuid(), EmpresaId = empresaId.Value, PedidoId = pedidoId,
                        PagamentoId = p.PagamentoId, PagamentoExternoId = p.PagamentoExternoId, EstornoExternoId = $"chargeback-{pagamento.Id}",
                        Valor = restante, Motivo = "Contestação confirmada pelo Mercado Pago", UsuarioNome = "Mercado Pago", CriadoEm = Agora };
                    await ConfirmarAsync(contestacao, p, estornos, token);
                    await repo.AdicionarAsync(contestacao, token);
                }
                else if (restante != 0)
                    throw new InvalidOperationException("A lista de estornos ainda não confirma o total devolvido; repetir o webhook.");
                return true;
            }, ct);
        return true;
    }

    private Task<bool> ImportarAsync(Guid empresaId, Guid pedidoId, RecebimentoOnline recebido,
        IReadOnlyList<EstornoMercadoPagoResult> respostas, CancellationToken ct) =>
        ComPedidoTravadoAsync(empresaId, pedidoId, async (pagamentos, estornos, token) =>
        {
            var p = pagamentos.Single(p => p.PagamentoId == recebido.PagamentoId);
            if (estornos.Any(e => e.PagamentoId == p.PagamentoId && e.Situacao == PedidoEstornoOnline.Pendente && e.EnviadoEm is not null && e.EstornoExternoId is null))
                return false;
            foreach (var r in respostas)
            {
                if (!RespostaConfere(r, p.PagamentoExternoId) || r.Status != "approved") continue;
                var existente = estornos.FirstOrDefault(e => e.PagamentoExternoId == p.PagamentoExternoId && e.EstornoExternoId == r.EstornoId);
                if (existente is not null)
                {
                    if (existente.Situacao == PedidoEstornoOnline.Pendente && existente.Valor == r.Valor)
                    {
                        await ConfirmarAsync(existente, p, estornos, token);
                        await repo.AtualizarAsync(existente, token);
                    }
                    continue;
                }
                var externo = new PedidoEstornoOnline { Id = Guid.NewGuid(), EmpresaId = empresaId, PedidoId = pedidoId,
                    PagamentoId = p.PagamentoId, PagamentoExternoId = p.PagamentoExternoId, EstornoExternoId = r.EstornoId,
                    Valor = r.Valor!.Value, Motivo = "Estorno identificado na consulta ao Mercado Pago", UsuarioNome = "Mercado Pago", CriadoEm = Agora };
                await ConfirmarAsync(externo, p, estornos, token);
                await repo.AdicionarAsync(externo, token);
                estornos.Add(externo);
            }
            return true;
        }, ct);

    private async Task ConfirmarAsync(PedidoEstornoOnline estorno, RecebimentoOnline pagamento, List<PedidoEstornoOnline> estornos, CancellationToken ct)
    {
        var total = Confirmado(estornos.Where(e => e.Id != estorno.Id), pagamento) + estorno.Valor;
        if (total > pagamento.Valor) throw Conflito("conciliacao_necessaria", "O provedor informou devoluções acima do recebimento. É necessário conferir a conciliação.");
        estorno.Situacao = PedidoEstornoOnline.Confirmado;
        estorno.ConfirmadoEm = Agora;
        estorno.Detalhe = "Devolução confirmada pelo Mercado Pago e registrada no Caixa.";
        var movimento = MovimentoCaixa.Criar(estorno.EmpresaId, "saida", estorno.Valor, Agora, pagamento.LojaId);
        // IDs enviados pelo cliente não podem colidir com um lançamento de outra origem no Caixa.
        movimento.Id = Guid.NewGuid();
        estorno.MovimentoCaixaId = movimento.Id;
        movimento.CriadoEm = Agora;
        movimento.Metodo = pagamento.Metodo;
        movimento.Categoria = "Devolução de pedido";
        movimento.Descricao = $"Mercado Pago: devolução do pedido {estorno.PedidoId.ToString("N")[..8].ToUpperInvariant()}. {estorno.Motivo}";
        movimento.Referencia = estorno.EstornoExternoId;
        movimento.RegistradoPorUserId = estorno.UsuarioId;
        movimento.RegistradoPorNome = estorno.UsuarioNome;
        movimento.Origem = PedidoEstornoManual.OrigemCaixa;
        await caixa.AddMovimentoAsync(movimento);
        await pedidos.AddEventoAsync(new PedidoEvento { Id = Guid.NewGuid(), PedidoId = estorno.PedidoId,
            Tipo = "devolucao_online", OcorridoEm = Agora, UsuarioId = estorno.UsuarioId, UsuarioNome = estorno.UsuarioNome,
            Origem = "mercadopago", Detalhes = $"Devolução confirmada: {estorno.Valor.ToString("C", Cultura.PtBr)}. Motivo: {estorno.Motivo}. Estorno: {estorno.EstornoExternoId}. Operação: {estorno.Id}" });
        if (total == pagamento.Valor) await repo.MarcarCobrancaEstornadaAsync(estorno.EmpresaId, pagamento.CobrancaId, Agora, ct);
    }

    private Task<T> ComPedidoTravadoAsync<T>(Guid empresaId, Guid pedidoId,
        Func<IReadOnlyList<RecebimentoOnline>, List<PedidoEstornoOnline>, CancellationToken, Task<T>> acao, CancellationToken ct) =>
        uow.ExecuteInTransactionSemRetryAsync(async token =>
        {
            UseCaseGuards.EnsureEmpresaId(empresaId);
            UseCaseGuards.EnsureNotEmpty(pedidoId, "PedidoId");
            await pedidos.TravarAsync(empresaId, pedidoId, token);
            await ConferirPedidoAsync(empresaId, pedidoId);
            var r = await acao(await repo.RecebimentosAsync(empresaId, pedidoId, token),
                (await repo.ListarAsync(empresaId, pedidoId, token)).ToList(), token);
            await uow.CommitAsync();
            return r;
        }, ct);

    private async Task ConferirPedidoAsync(Guid empresaId, Guid pedidoId)
    {
        UseCaseGuards.EnsureEmpresaId(empresaId);
        var pedido = await pedidos.GetByIdWithDetailsAsync(empresaId, pedidoId);
        if (pedido is null || pedido.EmpresaId != empresaId) throw new CobrancaPedidoNaoEncontradoException(pedidoId);
    }

    private static bool PagamentoConfere(PagamentoMercadoPago? p, RecebimentoOnline recebido, Guid pedidoId) =>
        p is not null && p.Id == recebido.PagamentoExternoId && Guid.TryParse(p.ExternalReference, out var referencia)
        && referencia == pedidoId && p.TransactionAmount == recebido.Valor && (p.Aprovado || p.Estornado);
    private static bool RespostaConfere(EstornoMercadoPagoResult r, string pagamentoId) =>
        !string.IsNullOrWhiteSpace(r.EstornoId) && r.EstornoId.Length <= 30 && r.EstornoId.All(char.IsAsciiDigit) && r.PagamentoId == pagamentoId
        && r.Valor > 0 && r.Valor == decimal.Round(r.Valor.Value, 2);
    private static decimal Confirmado(IEnumerable<PedidoEstornoOnline> estornos, RecebimentoOnline p) =>
        estornos.Where(e => e.PagamentoId == p.PagamentoId && e.Situacao == PedidoEstornoOnline.Confirmado).Sum(e => e.Valor);
    private static CobrancaPedidoConflitoException Conflito(string codigo, string mensagem) => new(codigo, mensagem);
    private static void ExigirGerente(NivelAcesso nivel)
    {
        if (nivel is not (NivelAcesso.Gerente or NivelAcesso.Admin or NivelAcesso.SuperAdmin))
            throw new UnauthorizedAccessException("Só a dona ou um gerente pode solicitar estorno pelo Mercado Pago.");
    }
}
