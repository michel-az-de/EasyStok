using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Financeiro.Pagamentos;
using EasyStock.Domain.Entities.Financeiro;
using EasyStock.Domain.Enums.Financeiro;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.UseCases.Financeiro;

/// <summary>
/// Issue 826: regra do Pix que entra por webhook numa parcela a receber, sem Docker. O controller só
/// mocka o caso de uso; aqui a lógica roda de verdade com repositórios substitutos.
/// </summary>
public class ReconciliarPixParcelaReceberUseCaseTests
{
    private const string Txid = "cr1234560123456789abcdef01";
    private static readonly Guid Empresa = Guid.NewGuid();

    private sealed record Cenario(
        ReconciliarPixParcelaReceberUseCase Uc,
        ContaReceber Conta,
        ParcelaReceber Parcela,
        IContaReceberRepository ContaRepo,
        ICaixaRepository CaixaRepo,
        IEfiPixService Pix,
        IUnitOfWork Uow,
        List<MovimentoCaixa> Movimentos);

    private static Cenario Montar(decimal valorParcela = 100m)
    {
        var conta = ContaReceber.Criar(Empresa, clienteId: null, Guid.NewGuid(), "Cliente", DateTime.UtcNow);
        conta.AdicionarParcela(1, valorParcela, DateTime.UtcNow.AddDays(30));
        conta.Emitir();
        var parcela = conta.Parcelas.First();
        parcela.ContaReceber = conta;
        parcela.AssociarPix(Txid, "copia-e-cola", "qr", DateTime.UtcNow.AddHours(1));

        var contaRepo = Substitute.For<IContaReceberRepository>();
        contaRepo.GetParcelaByEfiTxidAsync(Txid, Arg.Any<CancellationToken>()).Returns(parcela);
        var movimentos = new List<MovimentoCaixa>();
        var caixaRepo = Substitute.For<ICaixaRepository>();
        caixaRepo.AddMovimentoAsync(Arg.Do<MovimentoCaixa>(movimentos.Add));
        var pix = Substitute.For<IEfiPixService>();
        var uow = Substitute.For<IUnitOfWork>();
        var uc = new ReconciliarPixParcelaReceberUseCase(contaRepo, caixaRepo, pix, uow,
            Substitute.For<ILogger<ReconciliarPixParcelaReceberUseCase>>());
        return new Cenario(uc, conta, parcela, contaRepo, caixaRepo, pix, uow, movimentos);
    }

    private static ReconciliarPixParcelaReceberCommand Webhook(decimal? valor = 100m) =>
        new(Txid, valor, DateTime.UtcNow);

    [Fact]
    public async Task Pix_integral_baixa_a_parcela_e_lanca_uma_entrada_no_caixa()
    {
        var c = Montar();

        var r = await c.Uc.ExecuteAsync(Webhook());

        r.Reconciliado.Should().BeTrue();
        c.Parcela.Status.Should().Be(StatusParcela.Paga);
        c.Parcela.Pagamentos.Should().ContainSingle(p => p.GatewayTransactionId == Txid && p.Valor == 100m);
        c.Conta.Status.Should().Be(StatusContaFinanceira.Paga);
        var mov = c.Movimentos.Should().ContainSingle().Subject;
        mov.Tipo.Should().Be("entrada");
        mov.Valor.Should().Be(100m);
        mov.Metodo.Should().Be("pix");
        c.Parcela.Pagamentos.Single().MovimentoCaixaId.Should().Be(mov.Id);
        await c.Uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task Webhook_duplicado_do_mesmo_txid_nao_credita_duas_vezes()
    {
        var c = Montar();

        var primeiro = await c.Uc.ExecuteAsync(Webhook());
        var segundo = await c.Uc.ExecuteAsync(Webhook());

        primeiro.Reconciliado.Should().BeTrue();
        segundo.Reconciliado.Should().BeFalse();
        segundo.Motivo.Should().Be("ja reconciliado");
        c.Parcela.Pagamentos.Should().ContainSingle();
        c.Parcela.ValorPago.Should().Be(100m);
        c.Movimentos.Should().ContainSingle();
        await c.Uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task Txid_de_pagamento_estornado_nao_gera_segundo_pagamento()
    {
        var c = Montar();
        await c.Uc.ExecuteAsync(Webhook());
        c.Parcela.Pagamentos.Single().Estornar(null, "Teste");
        c.Parcela.AtualizarStatusPorPagamentos(DateTime.UtcNow);

        var r = await c.Uc.ExecuteAsync(Webhook());

        r.Reconciliado.Should().BeFalse();
        r.Motivo.Should().Be("pix estornado");
        c.Parcela.Pagamentos.Should().ContainSingle();
        c.Movimentos.Should().ContainSingle();
    }

    [Fact]
    public async Task Pix_parcial_registra_o_valor_recebido_e_deixa_parcela_parcialmente_paga()
    {
        var c = Montar();

        var r = await c.Uc.ExecuteAsync(Webhook(40m));

        r.Reconciliado.Should().BeTrue();
        c.Parcela.Status.Should().Be(StatusParcela.ParcialmentePaga);
        c.Parcela.Saldo.Should().Be(60m);
        c.Movimentos.Should().ContainSingle().Which.Valor.Should().Be(40m);
    }

    [Fact]
    public async Task Pix_acima_do_saldo_credita_so_o_saldo()
    {
        var c = Montar();

        var r = await c.Uc.ExecuteAsync(Webhook(150m));

        r.Reconciliado.Should().BeTrue();
        c.Parcela.Pagamentos.Single().Valor.Should().Be(100m);
        c.Parcela.ValorPago.Should().Be(100m);
        c.Movimentos.Should().ContainSingle().Which.Valor.Should().Be(100m);
    }

    [Fact]
    public async Task Sem_valor_no_payload_consulta_o_gateway_e_usa_o_valor_dele()
    {
        var c = Montar();
        c.Pix.ConsultarCobrancaAsync(Txid, Arg.Any<CancellationToken>())
            .Returns(new EfiCobrancaStatusResult(Txid, EfiCobrancaStatus.Concluida, ValorPago: 100m, PagoEm: DateTime.UtcNow));

        var r = await c.Uc.ExecuteAsync(Webhook(valor: null));

        r.Reconciliado.Should().BeTrue();
        await c.Pix.Received(1).ConsultarCobrancaAsync(Txid, Arg.Any<CancellationToken>());
        c.Parcela.Status.Should().Be(StatusParcela.Paga);
        c.Movimentos.Should().ContainSingle();
    }

    [Fact]
    public async Task Gateway_sem_cobranca_concluida_nao_baixa()
    {
        var c = Montar();
        c.Pix.ConsultarCobrancaAsync(Txid, Arg.Any<CancellationToken>())
            .Returns(new EfiCobrancaStatusResult(Txid, EfiCobrancaStatus.Ativa));

        var r = await c.Uc.ExecuteAsync(Webhook(valor: null));

        r.Reconciliado.Should().BeFalse();
        r.Motivo.Should().Be("status efi=Ativa");
        c.Parcela.Pagamentos.Should().BeEmpty();
        c.Movimentos.Should().BeEmpty();
        await c.Uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task Gateway_concluido_com_valor_zero_nao_baixa()
    {
        var c = Montar();
        c.Pix.ConsultarCobrancaAsync(Txid, Arg.Any<CancellationToken>())
            .Returns(new EfiCobrancaStatusResult(Txid, EfiCobrancaStatus.Concluida, ValorPago: 0m));

        var r = await c.Uc.ExecuteAsync(Webhook(valor: null));

        r.Motivo.Should().Be("valor zero");
        c.Movimentos.Should().BeEmpty();
    }

    [Fact]
    public async Task Parcela_cancelada_devolve_a_regra_de_dominio_sem_lancar_no_caixa()
    {
        var c = Montar();
        c.Parcela.Cancelar();

        var r = await c.Uc.ExecuteAsync(Webhook());

        r.Reconciliado.Should().BeFalse();
        r.Motivo.Should().Contain("cancelada");
        c.Movimentos.Should().BeEmpty();
        await c.Uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task Baixa_manual_de_outra_empresa_trata_o_txid_como_inexistente()
    {
        var c = Montar();

        var r = await c.Uc.ExecuteAsync(new ReconciliarPixParcelaReceberCommand(Txid, 100m, EmpresaId: Guid.NewGuid()));

        r.Reconciliado.Should().BeFalse();
        r.Motivo.Should().Be("txid nao pertence a esta parcela");
        c.Parcela.Pagamentos.Should().BeEmpty();
    }

    [Fact]
    public async Task Txid_desconhecido_nao_faz_nada()
    {
        var c = Montar();

        var r = await c.Uc.ExecuteAsync(new ReconciliarPixParcelaReceberCommand("cr-desconhecido", 100m));

        r.Motivo.Should().Be("parcela nao encontrada");
        c.Movimentos.Should().BeEmpty();
    }
}
