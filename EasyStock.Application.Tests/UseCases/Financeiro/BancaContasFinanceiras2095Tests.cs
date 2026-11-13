using EasyStock.Application.Common;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Financeiro.Categorias;
using EasyStock.Application.UseCases.Financeiro.ContasPagar;
using EasyStock.Application.UseCases.Financeiro.ContasReceber;
using EasyStock.Application.UseCases.Financeiro.Common;
using EasyStock.Application.UseCases.Financeiro.Pagamentos;
using EasyStock.Domain.Entities.Financeiro;
using EasyStock.Domain.Enums.Financeiro;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.UseCases.Financeiro;

/// <summary>
/// #2095 (banca do financeiro, pacote A): regras de pagamento, estorno, duplicidade e validação das contas a pagar e
/// a receber. Cada teste descreve o comportamento correto; os ids F* são os do relatório da banca de 27/10/2026.
/// </summary>
public class BancaContasFinanceiras2095Tests
{
    private static readonly Guid Empresa = Guid.NewGuid();

    // ── montagem ────────────────────────────────────────────────────────────

    private static ParcelaPagar ParcelaPagar(bool emitir = true, decimal valor = 100m)
    {
        var conta = ContaPagar.Criar(Empresa, null, Guid.NewGuid(), "Aluguel", DateTime.UtcNow);
        conta.AdicionarParcela(1, valor, DateTime.UtcNow.AddDays(10));
        conta.AdicionarParcela(2, valor, DateTime.UtcNow.AddDays(40));
        if (emitir) conta.Emitir();
        var parcela = conta.Parcelas.First(p => p.Numero == 1);
        parcela.ContaPagar = conta;
        return parcela;
    }

    private static ParcelaReceber ParcelaReceber(bool emitir = true, decimal valor = 100m)
    {
        var conta = ContaReceber.Criar(Empresa, null, Guid.NewGuid(), "Encomenda", DateTime.UtcNow);
        conta.AdicionarParcela(1, valor, DateTime.UtcNow.AddDays(10));
        conta.AdicionarParcela(2, valor, DateTime.UtcNow.AddDays(40));
        if (emitir) conta.Emitir();
        var parcela = conta.Parcelas.First(p => p.Numero == 1);
        parcela.ContaReceber = conta;
        return parcela;
    }

    private static (RegistrarPagamentoParcelaPagarUseCase Uc, ICaixaRepository Caixa) Pagar(ParcelaPagar parcela)
    {
        var repo = Substitute.For<IContaPagarRepository>();
        repo.GetParcelaWithContaAsync(Empresa, parcela.Id, Arg.Any<CancellationToken>()).Returns(parcela);
        repo.GetByIdAsync(Empresa, parcela.ContaPagarId, Arg.Any<CancellationToken>()).Returns(parcela.ContaPagar);
        var caixa = Substitute.For<ICaixaRepository>();
        return (new RegistrarPagamentoParcelaPagarUseCase(repo, caixa, Substitute.For<IUnitOfWork>(),
            Substitute.For<ILogger<RegistrarPagamentoParcelaPagarUseCase>>()), caixa);
    }

    private static (RegistrarPagamentoParcelaReceberUseCase Uc, ICaixaRepository Caixa) Receber(ParcelaReceber parcela)
    {
        var repo = Substitute.For<IContaReceberRepository>();
        repo.GetParcelaWithContaAsync(Empresa, parcela.Id, Arg.Any<CancellationToken>()).Returns(parcela);
        var caixa = Substitute.For<ICaixaRepository>();
        return (new RegistrarPagamentoParcelaReceberUseCase(repo, caixa, Substitute.For<IUnitOfWork>(),
            Substitute.For<ILogger<RegistrarPagamentoParcelaReceberUseCase>>()), caixa);
    }

    // ── F1: pagamento só em conta emitida ───────────────────────────────────

    [Fact]
    public async Task F1_pagamento_em_conta_a_pagar_rascunho_e_recusado_sem_tocar_o_caixa()
    {
        var parcela = ParcelaPagar(emitir: false);
        var (uc, caixa) = Pagar(parcela);

        var acao = () => uc.ExecuteAsync(new RegistrarPagamentoParcelaPagarCommand(Empresa, parcela.Id, 100m, "pix"));

        (await acao.Should().ThrowAsync<UseCaseValidationException>()).Which.Message.Should().Contain("Rascunho");
        parcela.Pagamentos.Should().BeEmpty();
        await caixa.DidNotReceive().AddMovimentoAsync(Arg.Any<MovimentoCaixa>());
    }

    [Fact]
    public async Task F1_recebimento_em_conta_a_receber_rascunho_e_recusado_sem_tocar_o_caixa()
    {
        var parcela = ParcelaReceber(emitir: false);
        var (uc, caixa) = Receber(parcela);

        var acao = () => uc.ExecuteAsync(new RegistrarPagamentoParcelaReceberCommand(Empresa, parcela.Id, 100m, "pix"));

        (await acao.Should().ThrowAsync<UseCaseValidationException>()).Which.Message.Should().Contain("Rascunho");
        await caixa.DidNotReceive().AddMovimentoAsync(Arg.Any<MovimentoCaixa>());
    }

    [Fact]
    public async Task F1_recebimento_em_conta_a_receber_cancelada_e_recusado()
    {
        var parcela = ParcelaReceber();
        parcela.ContaReceber!.Cancelar("cliente desistiu", null);
        // Defesa: mesmo que a parcela tenha ficado viva (dado legado), a conta cancelada não recebe.
        parcela.Status = StatusParcela.Pendente;
        var (uc, caixa) = Receber(parcela);

        var acao = () => uc.ExecuteAsync(new RegistrarPagamentoParcelaReceberCommand(Empresa, parcela.Id, 100m, "pix"));

        (await acao.Should().ThrowAsync<UseCaseValidationException>()).Which.Message.Should().Contain("Cancelada");
        await caixa.DidNotReceive().AddMovimentoAsync(Arg.Any<MovimentoCaixa>());
    }

    [Fact]
    public async Task F1_remover_parcela_com_pagamento_confirmado_e_recusado()
    {
        var parcela = ParcelaPagar(emitir: false);
        var conta = parcela.ContaPagar!;
        // Estado que a API deixava criar antes da F1: pagamento em conta Rascunho.
        parcela.RegistrarPagamento(PagamentoParcela.CriarConfirmado(Empresa, TipoLadoFinanceiro.Pagar, 100m, "pix", DateTime.UtcNow), DateTime.UtcNow);
        var repo = Substitute.For<IContaPagarRepository>();
        repo.GetByIdAsync(Empresa, conta.Id, Arg.Any<CancellationToken>()).Returns(conta);
        var uc = new RemoverParcelaContaPagarUseCase(repo, Substitute.For<IUnitOfWork>());

        var acao = () => uc.ExecuteAsync(new RemoverParcelaContaPagarCommand(Empresa, conta.Id, parcela.Id));

        (await acao.Should().ThrowAsync<UseCaseValidationException>()).Which.Message.Should().Contain("pagamento");
        conta.Parcelas.Should().Contain(parcela);
    }

    [Fact]
    public async Task F1_remover_parcela_de_conta_a_receber_com_recebimento_e_recusado()
    {
        var parcela = ParcelaReceber(emitir: false);
        var conta = parcela.ContaReceber!;
        parcela.RegistrarPagamento(PagamentoParcela.CriarConfirmado(Empresa, TipoLadoFinanceiro.Receber, 30m, "pix", DateTime.UtcNow), DateTime.UtcNow);
        var repo = Substitute.For<IContaReceberRepository>();
        repo.GetByIdAsync(Empresa, conta.Id, Arg.Any<CancellationToken>()).Returns(conta);
        var uc = new RemoverParcelaContaReceberUseCase(repo, Substitute.For<IUnitOfWork>());

        var acao = () => uc.ExecuteAsync(new RemoverParcelaContaReceberCommand(Empresa, conta.Id, parcela.Id));

        await acao.Should().ThrowAsync<UseCaseValidationException>();
        conta.Parcelas.Should().Contain(parcela);
    }

    // ── F2: estorno em conta cancelada ──────────────────────────────────────

    [Fact]
    public async Task F2_estorno_em_conta_a_pagar_cancelada_cancela_a_parcela_e_nao_reabre_a_conta()
    {
        var parcela = ParcelaPagar();
        var conta = parcela.ContaPagar!;
        var pag = PagamentoParcela.CriarConfirmado(Empresa, TipoLadoFinanceiro.Pagar, 100m, "pix", DateTime.UtcNow);
        parcela.RegistrarPagamento(pag, DateTime.UtcNow);
        conta.AtualizarStatusPorParcelas();
        conta.Cancelar("desisti", null); // parcela 2 cancela; a 1, paga, fica
        var repo = Substitute.For<IContaPagarRepository>();
        repo.GetParcelaWithContaAsync(Empresa, parcela.Id, Arg.Any<CancellationToken>()).Returns(parcela);
        var estornar = new EstornarPagamentoParcelaPagarUseCase(repo, Substitute.For<ICaixaRepository>(),
            Substitute.For<IUnitOfWork>(), Substitute.For<ILogger<EstornarPagamentoParcelaPagarUseCase>>());

        await estornar.ExecuteAsync(new EstornarPagamentoParcelaPagarCommand(Empresa, parcela.Id, pag.Id, "engano"));

        parcela.Status.Should().Be(StatusParcela.Cancelada);
        conta.Status.Should().Be(StatusContaFinanceira.Cancelada);
        conta.Pendente.Should().Be(0m);
        var (pagar, caixa) = Pagar(parcela);
        var novo = () => pagar.ExecuteAsync(new RegistrarPagamentoParcelaPagarCommand(Empresa, parcela.Id, 100m, "pix"));
        await novo.Should().ThrowAsync<UseCaseValidationException>();
        await caixa.DidNotReceive().AddMovimentoAsync(Arg.Any<MovimentoCaixa>());
    }

    [Fact]
    public async Task F2_estorno_em_conta_a_receber_cancelada_cancela_a_parcela()
    {
        var parcela = ParcelaReceber();
        var conta = parcela.ContaReceber!;
        var pag = PagamentoParcela.CriarConfirmado(Empresa, TipoLadoFinanceiro.Receber, 100m, "pix", DateTime.UtcNow);
        parcela.RegistrarPagamento(pag, DateTime.UtcNow);
        conta.AtualizarStatusPorParcelas();
        conta.Cancelar("desisti", null);
        var repo = Substitute.For<IContaReceberRepository>();
        repo.GetParcelaWithContaAsync(Empresa, parcela.Id, Arg.Any<CancellationToken>()).Returns(parcela);
        var estornar = new EstornarPagamentoParcelaReceberUseCase(repo, Substitute.For<ICaixaRepository>(),
            Substitute.For<IUnitOfWork>(), Substitute.For<ILogger<EstornarPagamentoParcelaReceberUseCase>>());

        await estornar.ExecuteAsync(new EstornarPagamentoParcelaReceberCommand(Empresa, parcela.Id, pag.Id, "engano"));

        parcela.Status.Should().Be(StatusParcela.Cancelada);
        conta.Status.Should().Be(StatusContaFinanceira.Cancelada);
    }

    // ── F6: documento repetido ──────────────────────────────────────────────

    [Fact]
    public async Task F6_documento_repetido_em_conta_manual_nao_devolve_a_conta_antiga()
    {
        var repo = Substitute.For<IContaPagarRepository>();
        var categorias = Substitute.For<ICategoriaFinanceiraRepository>();
        var cat = CategoriaFinanceira.Criar(Empresa, "Compras", TipoCategoriaFinanceira.Despesa);
        categorias.GetByIdAsync(Empresa, cat.Id, Arg.Any<CancellationToken>()).Returns(cat);
        var existente = ContaPagar.Criar(Empresa, Guid.NewGuid(), cat.Id, "NF 123 Atacadão", DateTime.UtcNow, documentoReferencia: "NF 123");
        repo.GetByDocumentoReferenciaAsync(Empresa, "NF 123", Arg.Any<CancellationToken>()).Returns(existente);
        var uc = new CriarContaPagarUseCase(repo, categorias, Substitute.For<ICentroCustoRepository>(),
            Substitute.For<IUnitOfWork>(), Substitute.For<ILogger<CriarContaPagarUseCase>>());

        var acao = () => uc.ExecuteAsync(new CriarContaPagarCommand(Empresa, null, cat.Id, "NF 123 Hortifruti",
            DateTime.UtcNow, [new ParcelaSpec(1, 800m, DateTime.UtcNow.AddDays(15))], DocumentoReferencia: "NF 123"));

        await acao.Should().ThrowAsync<Exception>().WithMessage("Já existe conta com o documento NF 123*");
        await repo.DidNotReceive().AddAsync(Arg.Any<ContaPagar>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task F6_conta_de_integracao_com_mesma_origem_continua_idempotente()
    {
        var repo = Substitute.For<IContaReceberRepository>();
        var categorias = Substitute.For<ICategoriaFinanceiraRepository>();
        var cat = CategoriaFinanceira.Criar(Empresa, "Vendas", TipoCategoriaFinanceira.Receita);
        categorias.GetByIdAsync(Empresa, cat.Id, Arg.Any<CancellationToken>()).Returns(cat);
        var pedidoId = Guid.NewGuid();
        var existente = ContaReceber.Criar(Empresa, null, cat.Id, "Pedido", DateTime.UtcNow,
            origem: OrigemContaFinanceira.Pedido, origemRefId: pedidoId, documentoReferencia: $"pedido:{pedidoId}");
        repo.GetByDocumentoReferenciaAsync(Empresa, $"pedido:{pedidoId}", Arg.Any<CancellationToken>()).Returns(existente);
        repo.GetByOrigemAsync(Empresa, OrigemContaFinanceira.Pedido, pedidoId, Arg.Any<CancellationToken>()).Returns(existente);
        var uc = new CriarContaReceberUseCase(repo, categorias, Substitute.For<ICentroCustoRepository>(),
            Substitute.For<IUnitOfWork>(), Substitute.For<ILogger<CriarContaReceberUseCase>>());

        var r = await uc.ExecuteAsync(new CriarContaReceberCommand(Empresa, null, cat.Id, "Pedido", DateTime.UtcNow,
            [new ParcelaSpec(1, 50m, DateTime.UtcNow.AddDays(7))], Origem: OrigemContaFinanceira.Pedido,
            OrigemRefId: pedidoId, DocumentoReferencia: $"pedido:{pedidoId}"));

        r.Id.Should().Be(existente.Id);
    }

    // ── F7: tamanho e faixa ─────────────────────────────────────────────────

    private static CriarContaPagarUseCase CriarPagar(out CategoriaFinanceira cat)
    {
        var categorias = Substitute.For<ICategoriaFinanceiraRepository>();
        cat = CategoriaFinanceira.Criar(Empresa, "Compras", TipoCategoriaFinanceira.Despesa);
        categorias.GetByIdAsync(Empresa, cat.Id, Arg.Any<CancellationToken>()).Returns(cat);
        return new CriarContaPagarUseCase(Substitute.For<IContaPagarRepository>(), categorias, Substitute.For<ICentroCustoRepository>(),
            Substitute.For<IUnitOfWork>(), Substitute.For<ILogger<CriarContaPagarUseCase>>());
    }

    [Fact]
    public async Task F7_descricao_acima_de_200_caracteres_vira_400()
    {
        var uc = CriarPagar(out var cat);

        var acao = () => uc.ExecuteAsync(new CriarContaPagarCommand(Empresa, null, cat.Id, new string('x', 201),
            DateTime.UtcNow, [new ParcelaSpec(1, 10m, DateTime.UtcNow.AddDays(1))]));

        (await acao.Should().ThrowAsync<UseCaseValidationException>()).Which.Message.Should().Contain("200");
    }

    [Fact]
    public async Task F7_documento_acima_de_120_caracteres_vira_400()
    {
        var uc = CriarPagar(out var cat);

        var acao = () => uc.ExecuteAsync(new CriarContaPagarCommand(Empresa, null, cat.Id, "Conta",
            DateTime.UtcNow, [new ParcelaSpec(1, 10m, DateTime.UtcNow.AddDays(1))], DocumentoReferencia: new string('9', 121)));

        await acao.Should().ThrowAsync<UseCaseValidationException>();
    }

    [Fact]
    public async Task F7_valor_de_parcela_acima_do_teto_vira_400()
    {
        var uc = CriarPagar(out var cat);

        var acao = () => uc.ExecuteAsync(new CriarContaPagarCommand(Empresa, null, cat.Id, "Conta",
            DateTime.UtcNow, [new ParcelaSpec(1, 1_000_000_000_000_000m, DateTime.UtcNow.AddDays(1))]));

        (await acao.Should().ThrowAsync<UseCaseValidationException>()).Which.Message.Should().Contain("999.999.999,99");
    }

    [Fact]
    public async Task F7_metodo_planejado_acima_de_20_caracteres_vira_400()
    {
        var uc = CriarPagar(out var cat);

        var acao = () => uc.ExecuteAsync(new CriarContaPagarCommand(Empresa, null, cat.Id, "Conta",
            DateTime.UtcNow, [new ParcelaSpec(1, 10m, DateTime.UtcNow.AddDays(1), new string('m', 21))]));

        await acao.Should().ThrowAsync<UseCaseValidationException>();
    }

    [Fact]
    public async Task F7_observacao_do_pagamento_acima_de_500_caracteres_vira_400()
    {
        var parcela = ParcelaPagar();
        var (uc, _) = Pagar(parcela);

        var acao = () => uc.ExecuteAsync(new RegistrarPagamentoParcelaPagarCommand(Empresa, parcela.Id, 10m, "pix",
            Observacao: new string('o', 501)));

        await acao.Should().ThrowAsync<UseCaseValidationException>();
    }

    [Fact]
    public async Task F7_pagina_enorme_e_normalizada_sem_estourar_o_deslocamento()
    {
        var repo = Substitute.For<IContaPagarRepository>();
        repo.ListarAsync(default, default, default, default, default, default, default, default, default, default, default, default, default)
            .ReturnsForAnyArgs(((IReadOnlyList<ContaPagar>)[], 0));
        var uc = new ListarContasPagarUseCase(repo);

        var r = await uc.ExecuteAsync(new ListarContasPagarQuery(Empresa, Page: int.MaxValue, PageSize: 200));

        ((long)(r.Page - 1) * r.PageSize).Should().BeLessThanOrEqualTo(int.MaxValue);
    }

    // ── F8: arredondamento e método ─────────────────────────────────────────

    [Fact]
    public async Task F8_valor_que_arredonda_para_zero_e_recusado()
    {
        var parcela = ParcelaReceber();
        var (uc, caixa) = Receber(parcela);

        var acao = () => uc.ExecuteAsync(new RegistrarPagamentoParcelaReceberCommand(Empresa, parcela.Id, 0.004m, "pix"));

        await acao.Should().ThrowAsync<UseCaseValidationException>();
        await caixa.DidNotReceive().AddMovimentoAsync(Arg.Any<MovimentoCaixa>());
    }

    [Fact]
    public async Task F8_movimento_do_caixa_tem_o_valor_arredondado_do_pagamento()
    {
        var parcela = ParcelaReceber();
        var (uc, caixa) = Receber(parcela);
        MovimentoCaixa? mov = null;
        caixa.When(c => c.AddMovimentoAsync(Arg.Any<MovimentoCaixa>())).Do(ci => mov = ci.Arg<MovimentoCaixa>());

        await uc.ExecuteAsync(new RegistrarPagamentoParcelaReceberCommand(Empresa, parcela.Id, 33.335m, "dinheiro"));

        mov!.Valor.Should().Be(33.34m);
        parcela.Pagamentos.Single().Valor.Should().Be(33.34m);
    }

    [Theory]
    [InlineData("bitcoin")]
    [InlineData("transferencia bancaria")]
    public async Task F8_metodo_fora_da_lista_do_caixa_e_recusado(string metodo)
    {
        var parcela = ParcelaPagar();
        var (uc, caixa) = Pagar(parcela);

        var acao = () => uc.ExecuteAsync(new RegistrarPagamentoParcelaPagarCommand(Empresa, parcela.Id, 10m, metodo));

        (await acao.Should().ThrowAsync<UseCaseValidationException>()).Which.Message.Should().Contain("Forma de pagamento");
        await caixa.DidNotReceive().AddMovimentoAsync(Arg.Any<MovimentoCaixa>());
    }

    [Fact]
    public async Task F8_metodo_da_lista_em_maiusculas_e_aceito()
    {
        var parcela = ParcelaPagar();
        var (uc, _) = Pagar(parcela);

        var r = await uc.ExecuteAsync(new RegistrarPagamentoParcelaPagarCommand(Empresa, parcela.Id, 10m, " PIX "));

        r.Should().NotBeNull();
        parcela.Pagamentos.Single().Metodo.Should().Be("pix");
    }

    // ── F9: data do pagamento ───────────────────────────────────────────────

    [Fact]
    public async Task F9_data_do_pagamento_so_com_o_dia_e_o_dia_civil_de_Brasilia()
    {
        var parcela = ParcelaPagar();
        var (uc, _) = Pagar(parcela);
        var ontem = HorarioBrasil.Hoje().AddDays(-1);

        await uc.ExecuteAsync(new RegistrarPagamentoParcelaPagarCommand(Empresa, parcela.Id, 10m, "pix",
            DataPagamento: ontem.ToDateTime(TimeOnly.MinValue)));

        var data = parcela.Pagamentos.Single().DataPagamento;
        data.Kind.Should().Be(DateTimeKind.Utc);
        HorarioBrasil.DataOperacional(data).Should().Be(ontem);
    }

    [Fact]
    public async Task F9_data_do_pagamento_futura_e_recusada()
    {
        var parcela = ParcelaReceber();
        var (uc, caixa) = Receber(parcela);
        var depoisDeAmanha = HorarioBrasil.Hoje().AddDays(2).ToDateTime(TimeOnly.MinValue);

        var acao = () => uc.ExecuteAsync(new RegistrarPagamentoParcelaReceberCommand(Empresa, parcela.Id, 10m, "pix",
            DataPagamento: depoisDeAmanha));

        (await acao.Should().ThrowAsync<UseCaseValidationException>()).Which.Message.Should().Contain("futura");
        await caixa.DidNotReceive().AddMovimentoAsync(Arg.Any<MovimentoCaixa>());
    }

    // ── F15: categoria, emitir duas vezes ───────────────────────────────────

    [Fact]
    public async Task F15_categoria_com_tipo_fora_do_enum_e_recusada()
    {
        var repo = Substitute.For<ICategoriaFinanceiraRepository>();
        var uc = new CriarCategoriaFinanceiraUseCase(repo, Substitute.For<IUnitOfWork>(),
            Substitute.For<ILogger<CriarCategoriaFinanceiraUseCase>>());

        var acao = () => uc.ExecuteAsync(new CriarCategoriaFinanceiraCommand(Empresa, "Estranha", (TipoCategoriaFinanceira)99));

        await acao.Should().ThrowAsync<UseCaseValidationException>();
        await repo.DidNotReceive().AddAsync(Arg.Any<CategoriaFinanceira>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task F15_nome_de_categoria_acima_de_80_caracteres_vira_400()
    {
        var uc = new CriarCategoriaFinanceiraUseCase(Substitute.For<ICategoriaFinanceiraRepository>(), Substitute.For<IUnitOfWork>(),
            Substitute.For<ILogger<CriarCategoriaFinanceiraUseCase>>());

        var acao = () => uc.ExecuteAsync(new CriarCategoriaFinanceiraCommand(Empresa, new string('c', 81), TipoCategoriaFinanceira.Despesa));

        await acao.Should().ThrowAsync<UseCaseValidationException>();
    }

    [Fact]
    public async Task F15_emitir_conta_a_pagar_ja_emitida_nao_grava_segundo_evento()
    {
        var parcela = ParcelaPagar();
        var conta = parcela.ContaPagar!;
        var repo = Substitute.For<IContaPagarRepository>();
        repo.GetByIdAsync(Empresa, conta.Id, Arg.Any<CancellationToken>()).Returns(conta);
        var uc = new EmitirContaPagarUseCase(repo, Substitute.For<IUnitOfWork>());

        var r = await uc.ExecuteAsync(new EmitirContaPagarCommand(Empresa, conta.Id));

        r.Should().NotBeNull();
        await repo.DidNotReceive().AddEventoAsync(Arg.Any<ContaFinanceiraEvento>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task F15_emitir_conta_a_receber_ja_emitida_nao_grava_segundo_evento()
    {
        var parcela = ParcelaReceber();
        var conta = parcela.ContaReceber!;
        var repo = Substitute.For<IContaReceberRepository>();
        repo.GetByIdAsync(Empresa, conta.Id, Arg.Any<CancellationToken>()).Returns(conta);
        var uc = new EmitirContaReceberUseCase(repo, Substitute.For<IUnitOfWork>());

        await uc.ExecuteAsync(new EmitirContaReceberCommand(Empresa, conta.Id));

        await repo.DidNotReceive().AddEventoAsync(Arg.Any<ContaFinanceiraEvento>(), Arg.Any<CancellationToken>());
    }
}
