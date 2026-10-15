using System.Text.Json;
using EasyStock.Application.Ports.Output.Integrations;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.UseCases.Caixa;
using EasyStock.Domain.Entities.Atendimento;

namespace EasyStock.Application.UseCases.Atendimento.Entregas;

public sealed class LogisticaUseCases(ILogisticaRepository repository, IUnitOfWork unitOfWork,
    ICaixaRepository caixa, IEnumerable<IFreteProvider> providers, ConfiguracaoLogistica configuracao, TimeProvider relogio)
{
    public async Task<ContextoLogisticaResult> ContextoAsync(Guid empresaId, DateOnly? dia, Guid? pedidoId, CancellationToken ct = default)
    {
        if (empresaId == Guid.Empty || configuracao.EmpresaDemonstracaoId == Guid.Empty || empresaId != configuracao.EmpresaDemonstracaoId)
            return new(false, Politica(null), [], [],
                "A contratação de transporte ainda não está conectada nesta empresa. Consulte o prestador e combine o frete com o cliente.");
        var pedidos = await repository.PedidosAsync(empresaId, dia, pedidoId, ct);
        if (pedidoId is not null && pedidos.Count > 0)
        {
            var diaDoPedido = HorarioBrasil.DataOperacional(pedidos[0].JanelaInicio);
            pedidos = await repository.PedidosAsync(empresaId, diaDoPedido, null, ct);
        }
        var ids = pedidos.Select(p => p.Id).ToHashSet();
        var despachos = await repository.DespachosAsync(empresaId, ct);
        return new(true, Politica(await repository.PoliticaAsync(empresaId, ct)), pedidos,
            despachos.Where(d => Ids(d).Any(ids.Contains)).Select(Resultado).ToList());
    }

    public async Task<IReadOnlyList<CotacaoLogisticaResult>> CotarAsync(Guid empresaId, CotarLogisticaInput input, CancellationToken ct = default)
    {
        ExigirDemonstracao(empresaId);
        var pedidos = await SelecionarAsync(empresaId, input.PedidoIds, ct);
        if (pedidos.Count > 1 && !input.Agrupar) throw new UseCaseValidationException("Para cotar vários pedidos juntos, habilite agrupar entregas.");
        var inicio = pedidos.Max(p => p.JanelaInicio);
        var fim = pedidos.Min(p => p.JanelaFim);
        if (inicio >= fim) throw new UseCaseValidationException("As janelas não coincidem. Separe os pedidos em entregas diferentes.");
        var ordenados = OrdenarPorProximidade(pedidos);
        var solicitacao = new FreteSolicitacao(empresaId, configuracao.Origem,
            ordenados.Select(p => new FretePedido(p.Id, new(p.Cliente, p.Endereco, p.Telefone, p.Latitude!.Value, p.Longitude!.Value), p.JanelaInicio, p.JanelaFim)).ToList(),
            Guid.NewGuid().ToString("N"));
        var result = new List<CotacaoLogisticaResult>();
        foreach (var provider in providers)
        {
            FreteCotacao cotacao;
            try { cotacao = await provider.CotarAsync(solicitacao, ct); }
            catch (HttpRequestException)
            {
                cotacao = new(provider.Provedor, "", 0, 0, Agora.AddMinutes(1), true, "O serviço não respondeu. Tente novamente ou escolha outra opção.");
            }
            var indisponibilidade = cotacao.Indisponibilidade;
            if (cotacao.ValorCentavos < 0 || cotacao.DuracaoMinutos <= 0)
                indisponibilidade ??= "O serviço não retornou preço e prazo válidos.";
            if (cotacao.DuracaoMinutos > (fim - inicio).TotalMinutes)
                indisponibilidade ??= "O prazo estimado não cabe na janela comum destes pedidos.";
            var entity = new CotacaoLogistica
            {
                EmpresaId = empresaId, Provedor = cotacao.Provedor, Referencia = cotacao.Referencia,
                ValorCentavos = cotacao.ValorCentavos, DuracaoMinutos = cotacao.DuracaoMinutos,
                ExpiraEm = cotacao.ExpiraEmUtc, CriadoEm = Agora, Simulado = cotacao.Simulado,
                Indisponibilidade = indisponibilidade, SolicitacaoJson = JsonSerializer.Serialize(solicitacao)
            };
            repository.Adicionar(entity);
            result.Add(new(entity.Id, entity.Provedor, entity.ValorCentavos, entity.DuracaoMinutos,
                entity.ExpiraEm, entity.Simulado, entity.Indisponibilidade, ordenados.Select(p => p.Id).ToList()));
        }
        if (input.IncluirManual)
        {
            if (input.ValorManualCentavos is < 0 or > 100000) throw new UseCaseValidationException("Informe um custo manual entre R$ 0,00 e R$ 1.000,00.");
            foreach (var alternativa in new[] { (Provedor: "em_maos", Valor: 0, Motivo: (string?)null),
                (Provedor: "manual", Valor: input.ValorManualCentavos ?? 0, Motivo: input.ValorManualCentavos is null ? "Informe o custo combinado com o entregador." : null) })
            {
                var entity = new CotacaoLogistica { EmpresaId = empresaId, Provedor = alternativa.Provedor,
                    Referencia = "manual:" + solicitacao.ChaveIdempotencia, SolicitacaoJson = JsonSerializer.Serialize(solicitacao),
                    ValorCentavos = alternativa.Valor, DuracaoMinutos = 15, ExpiraEm = Agora.AddMinutes(10),
                    CriadoEm = Agora, Simulado = false, Indisponibilidade = alternativa.Motivo };
                repository.Adicionar(entity);
                result.Add(new(entity.Id, entity.Provedor, entity.ValorCentavos, entity.DuracaoMinutos, entity.ExpiraEm,
                    false, entity.Indisponibilidade, ordenados.Select(p => p.Id).ToList()));
            }
        }
        await unitOfWork.CommitAsync();
        return result.OrderBy(c => c.Indisponibilidade is not null).ThenBy(c => c.ValorCentavos).ToList();
    }

    public Task<DespachoLogisticaResult> ContratarAsync(Guid empresaId, Guid usuarioId, ContratarLogisticaInput input, CancellationToken ct = default) =>
        ContratarInternoAsync(empresaId, usuarioId, input, false, ct);

    private async Task<DespachoLogisticaResult> ContratarInternoAsync(Guid empresaId, Guid usuarioId, ContratarLogisticaInput input, bool automatico, CancellationToken ct)
    {
        ExigirDemonstracao(empresaId);
        ValidarChave(input.ChaveIdempotencia);
        return await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            await repository.TravarAsync(empresaId, token);
            var despachos = await repository.DespachosAsync(empresaId, token);
            if (despachos.FirstOrDefault(d => d.ChaveIdempotencia == input.ChaveIdempotencia) is { } existente)
            {
                if (existente.CotacaoId != input.CotacaoId)
                {
                    var repetida = await repository.CotacaoAsync(empresaId, input.CotacaoId, token);
                    var idsRepetidos = repetida is null ? [] : JsonSerializer.Deserialize<FreteSolicitacao>(repetida.SolicitacaoJson)!.Pedidos.Select(p => p.PedidoId).ToArray();
                    if (!automatico || !Ids(existente).Order().SequenceEqual(idsRepetidos.Order()))
                        throw new UseCaseValidationException("Esta chave já foi usada para outra cotação.");
                }
                return Resultado(existente);
            }
            var cotacao = await repository.CotacaoAsync(empresaId, input.CotacaoId, token)
                ?? throw new UseCaseValidationException("Cotação não encontrada nesta empresa.");
            if (cotacao.ExpiraEm <= Agora) throw new UseCaseValidationException("A cotação venceu. Consulte os preços novamente.");
            if (cotacao.Indisponibilidade is not null) throw new UseCaseValidationException(cotacao.Indisponibilidade);
            var solicitacao = JsonSerializer.Deserialize<FreteSolicitacao>(cotacao.SolicitacaoJson)!;
            var ids = solicitacao.Pedidos.Select(p => p.PedidoId).ToList();
            if (automatico)
            {
                var politicaAtual = Politica(await repository.PoliticaAsync(empresaId, token));
                if (!politicaAtual.Automatico || cotacao.ValorCentavos > politicaAtual.TetoCentavos || (ids.Count > 1 && !politicaAtual.PermitirAgrupar))
                    throw new UseCaseValidationException("A política automática mudou. Consulte as opções novamente.");
            }
            if (await repository.TemViagemAtivaAsync(empresaId, ids, token))
                throw new UseCaseValidationException("Um pedido já está em uma viagem. Abra a viagem existente antes de contratar outro frete.");
            var atuais = await SelecionarAsync(empresaId, ids, token);
            foreach (var atual in atuais)
            {
                var anterior = solicitacao.Pedidos.Single(p => p.PedidoId == atual.Id);
                if (atual.Cliente != anterior.Destino.Nome || atual.Telefone != anterior.Destino.Telefone || atual.Endereco != anterior.Destino.Endereco || atual.Latitude != anterior.Destino.Latitude || atual.Longitude != anterior.Destino.Longitude
                    || atual.JanelaInicio != anterior.JanelaInicioUtc || atual.JanelaFim != anterior.JanelaFimUtc)
                    throw new UseCaseValidationException("Os dados da entrega mudaram. Faça uma nova cotação.");
            }
            if (despachos.Any(d => d.Status is not ("cancelado" or "devolvido") && Ids(d).Intersect(ids).Any()))
                throw new UseCaseValidationException("Um destes pedidos já está em uma entrega. Abra o acompanhamento existente.");
            FreteContratacao contratado;
            if (cotacao.Provedor is "manual" or "em_maos")
                contratado = new(cotacao.Referencia, null, "agendado", false);
            else
            {
                var provider = providers.SingleOrDefault(p => p.Provedor == cotacao.Provedor)
                    ?? throw new UseCaseValidationException("Serviço de entrega indisponível.");
                contratado = await provider.ContratarAsync(solicitacao, cotacao.Referencia, token);
            }
            var despacho = new DespachoLogistica
            {
                EmpresaId = empresaId, CotacaoId = cotacao.Id, UsuarioId = usuarioId,
                ChaveIdempotencia = input.ChaveIdempotencia, Provedor = cotacao.Provedor,
                Referencia = contratado.Referencia, RastreioUrl = contratado.RastreioUrl,
                ValorCentavos = cotacao.ValorCentavos, Simulado = contratado.Simulado,
                PedidoIdsJson = JsonSerializer.Serialize(ids), CriadoEm = Agora
            };
            despacho.ViagemId = await repository.CriarViagemAsync(empresaId, cotacao.Provedor, ids, token);
            repository.Adicionar(despacho);
            AdicionarEvento(despacho, "agendado", "Entrega registrada. O frete ainda não foi pago.", input.ChaveIdempotencia);
            await unitOfWork.CommitAsync();
            return Resultado(despacho);
        }, ct);
    }

    public async Task<DespachoLogisticaResult> ConfirmarPagamentoAsync(Guid empresaId, Guid usuarioId, Guid id, PagamentoLogisticaInput input, CancellationToken ct = default)
    {
        ExigirDemonstracao(empresaId);
        ValidarChave(input.ChaveIdempotencia);
        if (input.Meio is not ("pix" or "dinheiro" or "credito" or "debito" or "transferencia" or "outro"))
            throw new UseCaseValidationException("Selecione como o frete foi pago.");
        if (string.IsNullOrWhiteSpace(input.Referencia) || input.Referencia.Length > 120)
            throw new UseCaseValidationException("Informe a referência do pagamento, com até 120 caracteres.");
        return await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            await repository.TravarAsync(empresaId, token);
            var despacho = await ObterAsync(empresaId, id, token);
            if ((await repository.DespachosAsync(empresaId, token)).Any(d => d.Id != id && d.ChavePagamento == input.ChaveIdempotencia))
                throw new UseCaseValidationException("Esta chave de pagamento já foi usada para outra entrega.");
            if (despacho.MovimentoCaixaId is not null)
            {
                if (despacho.MeioPagamento != input.Meio || despacho.ReferenciaPagamento != input.Referencia)
                    throw new UseCaseValidationException("O frete já foi pago com outros dados. Consulte o Caixa.");
                return Resultado(despacho);
            }
            if (despacho.Status == "cancelado") throw new UseCaseValidationException("Entrega cancelada não aceita pagamento de frete.");
            if (despacho.ValorCentavos == 0) throw new UseCaseValidationException("Esta entrega não tem custo. Não é necessário registrar pagamento.");
            await caixa.TravarSaidasAsync(empresaId, null, token);
            await CaixaFechadoGuard.ExigirDiaAbertoAsync(caixa, empresaId, Agora, null, "O Caixa de hoje já foi fechado.");
            if (despacho.ValorCentavos / 100m > await repository.LimiteSaidaAsync(empresaId, Agora, GavetaCaixa.EhDinheiro(input.Meio), token))
                throw new UseCaseValidationException(input.Meio == "dinheiro" ? "Não há dinheiro suficiente na gaveta para pagar este frete." : "O saldo do Caixa é insuficiente para registrar este pagamento.");
            var movimento = MovimentoCaixa.Criar(empresaId, "saida", despacho.ValorCentavos / 100m, Agora);
            movimento.Categoria = "frete";
            movimento.Descricao = $"Frete {despacho.Provedor} · demonstração · {despacho.Id.ToString("N")[..8]}";
            movimento.Metodo = input.Meio;
            movimento.Referencia = input.Referencia;
            movimento.RegistradoPorUserId = usuarioId;
            await caixa.AddMovimentoAsync(movimento);
            despacho.MovimentoCaixaId = movimento.Id;
            despacho.ChavePagamento = input.ChaveIdempotencia;
            despacho.MeioPagamento = input.Meio;
            despacho.ReferenciaPagamento = input.Referencia;
            AdicionarEvento(despacho, "pagamento_confirmado", "Saída registrada no Caixa da demonstração.", input.ChaveIdempotencia);
            await unitOfWork.CommitAsync();
            return Resultado(despacho);
        }, ct);
    }

    public async Task<DespachoLogisticaResult> RegistrarEventoAsync(Guid empresaId, Guid id, EventoLogisticaInput input, CancellationToken ct = default)
    {
        ExigirDemonstracao(empresaId);
        ValidarChave(input.ChaveIdempotencia);
        if (input.Motivo?.Length > 500) throw new UseCaseValidationException("Use até 500 caracteres no motivo.");
        return await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            await repository.TravarAsync(empresaId, token);
            var despacho = await ObterAsync(empresaId, id, token);
            var eventos = Eventos(despacho);
            if (eventos.FirstOrDefault(e => e.ChaveIdempotencia == input.ChaveIdempotencia) is { } existente)
            {
                if (existente.Tipo != input.Tipo || existente.Motivo != input.Motivo)
                    throw new UseCaseValidationException("Esta chave já foi usada para outro evento.");
                return Resultado(despacho);
            }
            if (input.Tipo == "a_caminho" && despacho.Status == "agendado") await SelecionarAsync(empresaId, Ids(despacho), token, exigirPronto: true);
            Regra.Validar(() => { despacho.RegistrarEvento(input.Tipo, input.Motivo); return despacho; });
            AdicionarEvento(despacho, input.Tipo, input.Motivo, input.ChaveIdempotencia);
            await repository.AtualizarViagemAsync(despacho, input.Tipo, token);
            await unitOfWork.CommitAsync();
            return Resultado(despacho);
        }, ct);
    }

    public async Task<PoliticaLogisticaInput> ConfigurarPoliticaAsync(Guid empresaId, PoliticaLogisticaInput input, CancellationToken ct = default)
    {
        ExigirDemonstracao(empresaId);
        if (input.TetoCentavos is < 1 or > 100000) throw new UseCaseValidationException("Defina um limite entre R$ 0,01 e R$ 1.000,00.");
        return await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            await repository.TravarAsync(empresaId, token);
            var politica = await repository.PoliticaAsync(empresaId, token);
            if (politica is null) { politica = new() { EmpresaId = empresaId }; repository.Adicionar(politica); }
            politica.Automatico = input.Automatico;
            politica.TetoCentavos = input.TetoCentavos;
            politica.PermitirAgrupar = input.PermitirAgrupar;
            await unitOfWork.CommitAsync();
            return Politica(politica);
        }, ct);
    }

    public async Task<DespachoLogisticaResult> ContratarAutomaticoAsync(Guid empresaId, Guid usuarioId, CotarLogisticaInput input, string chaveIdempotencia, CancellationToken ct = default)
    {
        ExigirDemonstracao(empresaId);
        ValidarChave(chaveIdempotencia);
        if ((await repository.DespachosAsync(empresaId, ct)).FirstOrDefault(d => d.ChaveIdempotencia == chaveIdempotencia) is { } existente)
        {
            if (!Ids(existente).Order().SequenceEqual(input.PedidoIds.Order())) throw new UseCaseValidationException("Esta chave já foi usada para outros pedidos.");
            return Resultado(existente);
        }
        var politica = Politica(await repository.PoliticaAsync(empresaId, ct));
        if (!politica.Automatico) throw new UseCaseValidationException("A escolha automática está desativada. A responsável precisa configurá-la primeiro.");
        if (input.Agrupar && !politica.PermitirAgrupar) throw new UseCaseValidationException("A política não permite agrupar pedidos automaticamente.");
        var cotacoes = await CotarAsync(empresaId, input, ct);
        var melhor = cotacoes.FirstOrDefault(c => c.Provedor is not ("manual" or "em_maos") && c.Indisponibilidade is null && c.ValorCentavos <= politica.TetoCentavos)
            ?? throw new UseCaseValidationException("Nenhuma opção atende ao limite configurado. Escolha o frete manualmente.");
        return await ContratarInternoAsync(empresaId, usuarioId, new(melhor.Id, chaveIdempotencia), true, ct);
    }

    private async Task<IReadOnlyList<LogisticaPedido>> SelecionarAsync(Guid empresaId, IReadOnlyList<Guid> ids, CancellationToken ct, bool exigirPronto = false)
    {
        if (ids.Count is < 1 or > 15 || ids.Distinct().Count() != ids.Count) throw new UseCaseValidationException("Selecione de 1 a 15 pedidos diferentes.");
        var pedidos = (await repository.PedidosAsync(empresaId, null, null, ct)).Where(p => ids.Contains(p.Id)).ToList();
        if (pedidos.Count != ids.Count) throw new UseCaseValidationException("Pedido não encontrado ou sem janela de entrega nesta empresa.");
        if (pedidos.Any(p => p.Status is "cancelado" or "entregue" || p.ClienteBloqueado)) throw new UseCaseValidationException("Há pedido cancelado, já entregue ou cliente bloqueado.");
        if (exigirPronto && pedidos.Any(p => p.Status != "pronto")) throw new UseCaseValidationException("Todos os pedidos precisam estar prontos antes de sair.");
        if (pedidos.Any(p => string.IsNullOrWhiteSpace(p.Endereco) || p.Latitude is null || p.Longitude is null
            || !double.IsFinite(p.Latitude.Value) || !double.IsFinite(p.Longitude.Value) || Math.Abs(p.Latitude.Value) > 90 || Math.Abs(p.Longitude.Value) > 180))
            throw new UseCaseValidationException("Confirme o endereço e a localização de todos os pedidos antes de cotar.");
        if (pedidos.Any(p => string.IsNullOrWhiteSpace(p.Telefone))) throw new UseCaseValidationException("Informe o telefone de contato de todos os pedidos.");
        return pedidos;
    }

    private List<LogisticaPedido> OrdenarPorProximidade(IReadOnlyList<LogisticaPedido> pedidos)
    {
        var restantes = pedidos.ToList();
        var resultado = new List<LogisticaPedido>();
        var lat = configuracao.Origem.Latitude;
        var lng = configuracao.Origem.Longitude;
        while (restantes.Count > 0)
        {
            var proximo = restantes.OrderBy(p => Math.Pow(p.Latitude!.Value - lat, 2) + Math.Pow(p.Longitude!.Value - lng, 2)).ThenBy(p => p.Id).First();
            resultado.Add(proximo); restantes.Remove(proximo); lat = proximo.Latitude!.Value; lng = proximo.Longitude!.Value;
        }
        return resultado;
    }

    private async Task<DespachoLogistica> ObterAsync(Guid empresaId, Guid id, CancellationToken ct) =>
        await repository.DespachoAsync(empresaId, id, ct) ?? throw new UseCaseValidationException("Entrega não encontrada nesta empresa.");
    private void ExigirDemonstracao(Guid empresaId)
    {
        if (empresaId == Guid.Empty || configuracao.EmpresaDemonstracaoId == Guid.Empty || empresaId != configuracao.EmpresaDemonstracaoId)
            throw new UseCaseValidationException("Este laboratório está disponível somente na empresa de demonstração configurada.");
    }
    private static void ValidarChave(string chave)
    {
        if (string.IsNullOrWhiteSpace(chave) || chave.Length > 100) throw new UseCaseValidationException("Informe uma chave de operação válida, com até 100 caracteres.");
    }
    private void AdicionarEvento(DespachoLogistica despacho, string tipo, string? motivo, string chave)
    {
        var eventos = Eventos(despacho).ToList();
        eventos.Add(new(tipo, motivo, Agora, chave));
        despacho.EventosJson = JsonSerializer.Serialize(eventos);
        repository.RegistrarAvisoDemonstracao(despacho.EmpresaId, despacho.Id, tipo,
            JsonSerializer.Serialize(new { demonstracao = true, envioExterno = false, pedidoIds = Ids(despacho), tipo, motivo, rastreioUrl = despacho.RastreioUrl }));
    }
    private DateTime Agora => relogio.GetUtcNow().UtcDateTime;
    private static PoliticaLogisticaInput Politica(PoliticaLogistica? p) => new(p?.Automatico ?? false, p?.TetoCentavos ?? 3000, p?.PermitirAgrupar ?? false);
    private static IReadOnlyList<Guid> Ids(DespachoLogistica d) => JsonSerializer.Deserialize<List<Guid>>(d.PedidoIdsJson)!;
    private static IReadOnlyList<LogisticaEvento> Eventos(DespachoLogistica d) => JsonSerializer.Deserialize<List<LogisticaEvento>>(d.EventosJson)!;
    private static DespachoLogisticaResult Resultado(DespachoLogistica d) => new(d.Id, d.Provedor, d.Status, d.ValorCentavos, d.MovimentoCaixaId is not null, d.Simulado, d.RastreioUrl, Ids(d), d.CriadoEm, Eventos(d), d.ViagemId);
}
