using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Exceptions;
using EasyStock.Domain.ValueObjects;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities;

public class FaturaTests
{
    private static (DadosFaturado faturado, DadosEmissor emissor) StubsDados() => (
        new DadosFaturado(Nome: "Cliente Teste", Documento: "12345678900"),
        new DadosEmissor(Nome: "Empresa Teste")
    );

    [Fact]
    public void Criar_define_estado_inicial_rascunho_e_carimba_datas()
    {
        var (faturado, emissor) = StubsDados();
        var f = Fatura.Criar(
            empresaId: Guid.NewGuid(),
            numero: "2026-000001",
            dadosFaturado: faturado,
            dadosEmissor: emissor,
            origem: OrigemFatura.Avulsa,
            dataEmissao: DateTime.UtcNow,
            dataVencimento: DateTime.UtcNow.AddDays(7));

        f.Id.Should().NotBeEmpty();
        f.Status.Should().Be(StatusFatura.Rascunho);
        f.Total.Should().Be(0);
        f.Moeda.Should().Be("BRL");
        f.Itens.Should().BeEmpty();
        f.Pagamentos.Should().BeEmpty();
        f.CriadoEm.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Criar_rejeita_vencimento_antes_da_emissao()
    {
        var (faturado, emissor) = StubsDados();
        var act = () => Fatura.Criar(
            empresaId: Guid.NewGuid(),
            numero: "2026-000001",
            dadosFaturado: faturado,
            dadosEmissor: emissor,
            origem: OrigemFatura.Avulsa,
            dataEmissao: DateTime.UtcNow,
            dataVencimento: DateTime.UtcNow.AddDays(-1));

        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void AdicionarItem_recalcula_totais()
    {
        var f = NovaFaturaRascunho();

        f.AdicionarItem("Servico A", quantidade: 2, precoUnitario: 50m);
        f.AdicionarItem("Taxa entrega", quantidade: 1, precoUnitario: 10m, tipo: TipoItemFatura.Taxa);
        f.AdicionarItem("Desconto", quantidade: 1, precoUnitario: 5m, tipo: TipoItemFatura.Desconto);

        f.SubTotal.Should().Be(100m);
        f.Acrescimos.Should().Be(10m);
        f.Descontos.Should().Be(5m);
        f.Total.Should().Be(105m); // 100 - 5 + 10
        f.Itens.Should().HaveCount(3);
    }

    [Fact]
    public void Emitir_falha_sem_itens()
    {
        var f = NovaFaturaRascunho();
        var act = () => f.Emitir();
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void Emitir_transiciona_para_emitida()
    {
        var f = NovaFaturaRascunho();
        f.AdicionarItem("Servico", 1, 100m);
        f.Emitir();
        f.Status.Should().Be(StatusFatura.Emitida);
    }

    [Fact]
    public void Emitir_idempotente_quando_ja_emitida()
    {
        var f = NovaFaturaRascunho();
        f.AdicionarItem("X", 1, 100m);
        f.Emitir();
        var act = () => f.Emitir();
        act.Should().NotThrow();
        f.Status.Should().Be(StatusFatura.Emitida);
    }

    [Fact]
    public void RegistrarPagamento_parcial_marca_parcialmente_paga()
    {
        var f = NovaFaturaRascunho();
        f.AdicionarItem("Servico", 1, 100m);
        f.Emitir();

        var pag = FaturaPagamento.CriarConfirmado(f.Id, "manual", 30m, "Manual", Guid.NewGuid());
        f.RegistrarPagamento(pag);

        f.Status.Should().Be(StatusFatura.ParcialmentePaga);
        f.TotalPago.Should().Be(30m);
        f.Pendente.Should().Be(70m);
    }

    [Fact]
    public void RegistrarPagamento_total_marca_paga_e_carimba_data()
    {
        var f = NovaFaturaRascunho();
        f.AdicionarItem("Servico", 1, 100m);
        f.Emitir();

        f.RegistrarPagamento(FaturaPagamento.CriarConfirmado(f.Id, "pix", 100m, "EfiPix", Guid.NewGuid()));

        f.Status.Should().Be(StatusFatura.Paga);
        f.DataPagamentoTotal.Should().NotBeNull();
        f.DataPagamentoTotal!.Value.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
        f.Pendente.Should().Be(0);
    }

    [Fact]
    public void RegistrarPagamento_pendente_nao_altera_status_para_paga()
    {
        var f = NovaFaturaRascunho();
        f.AdicionarItem("Servico", 1, 100m);
        f.Emitir();

        f.RegistrarPagamento(FaturaPagamento.CriarPendente(f.Id, "pix", 100m, "EfiPix", Guid.NewGuid()));

        f.Status.Should().Be(StatusFatura.Emitida);
        f.TotalPago.Should().Be(0);
    }

    [Fact]
    public void RegistrarPagamento_em_rascunho_lanca()
    {
        var f = NovaFaturaRascunho();
        f.AdicionarItem("X", 1, 100m);

        var act = () => f.RegistrarPagamento(FaturaPagamento.CriarConfirmado(f.Id, "manual", 100m, "Manual", Guid.NewGuid()));
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void RegistrarPagamento_em_cancelada_lanca()
    {
        var f = NovaFaturaRascunho();
        f.AdicionarItem("X", 1, 100m);
        f.Emitir();
        f.Cancelar("teste");

        var act = () => f.RegistrarPagamento(FaturaPagamento.CriarConfirmado(f.Id, "manual", 100m, "Manual", Guid.NewGuid()));
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void Cancelar_de_paga_lanca()
    {
        var f = NovaFaturaRascunho();
        f.AdicionarItem("X", 1, 100m);
        f.Emitir();
        f.RegistrarPagamento(FaturaPagamento.CriarConfirmado(f.Id, "pix", 100m, "EfiPix", Guid.NewGuid()));

        var act = () => f.Cancelar();
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void Cancelar_e_idempotente_e_anota_motivo()
    {
        var f = NovaFaturaRascunho();
        f.AdicionarItem("X", 1, 100m);
        f.Emitir();

        f.Cancelar("erro de digitacao");
        f.Status.Should().Be(StatusFatura.Cancelada);
        f.Observacoes.Should().Contain("erro de digitacao");

        var act = () => f.Cancelar("outro");
        act.Should().NotThrow();
    }

    [Fact]
    public void AdicionarItem_em_paga_lanca()
    {
        var f = NovaFaturaRascunho();
        f.AdicionarItem("X", 1, 100m);
        f.Emitir();
        f.RegistrarPagamento(FaturaPagamento.CriarConfirmado(f.Id, "pix", 100m, "EfiPix", Guid.NewGuid()));

        var act = () => f.AdicionarItem("Y", 1, 10m);
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void MarcarVencidaSeAplicavel_marca_quando_passou_vencimento_sem_pagamento()
    {
        var (faturado, emissor) = StubsDados();
        var f = Fatura.Criar(
            empresaId: Guid.NewGuid(),
            numero: "2026-000099",
            dadosFaturado: faturado,
            dadosEmissor: emissor,
            origem: OrigemFatura.Assinatura,
            dataEmissao: DateTime.UtcNow.AddDays(-10),
            dataVencimento: DateTime.UtcNow.AddDays(-3));
        f.AdicionarItem("Mensalidade", 1, 100m);
        f.Emitir();

        f.MarcarVencidaSeAplicavel();
        f.Status.Should().Be(StatusFatura.Vencida);
    }

    [Fact]
    public void MarcarVencidaSeAplicavel_naomarca_se_paga()
    {
        var (faturado, emissor) = StubsDados();
        var f = Fatura.Criar(
            empresaId: Guid.NewGuid(),
            numero: "2026-000099",
            dadosFaturado: faturado,
            dadosEmissor: emissor,
            origem: OrigemFatura.Assinatura,
            dataEmissao: DateTime.UtcNow.AddDays(-10),
            dataVencimento: DateTime.UtcNow.AddDays(-3));
        f.AdicionarItem("Mensalidade", 1, 100m);
        f.Emitir();
        f.RegistrarPagamento(FaturaPagamento.CriarConfirmado(f.Id, "pix", 100m, "EfiPix", Guid.NewGuid()));

        f.MarcarVencidaSeAplicavel();
        f.Status.Should().Be(StatusFatura.Paga);
    }

    [Fact]
    public void VinculaTicket_atualiza_referencia_e_retorna_true_quando_diferente()
    {
        var f = NovaFaturaRascunho();
        var ticketId = Guid.NewGuid();

        f.VinculaTicket(ticketId).Should().BeTrue();
        f.TicketRelacionadoId.Should().Be(ticketId);

        // idempotente
        f.VinculaTicket(ticketId).Should().BeFalse();
    }

    private static Fatura NovaFaturaRascunho()
    {
        var (faturado, emissor) = StubsDados();
        return Fatura.Criar(
            empresaId: Guid.NewGuid(),
            numero: "2026-000001",
            dadosFaturado: faturado,
            dadosEmissor: emissor,
            origem: OrigemFatura.Avulsa,
            dataEmissao: DateTime.UtcNow,
            dataVencimento: DateTime.UtcNow.AddDays(7));
    }
}

public class FaturaPagamentoTests
{
    [Fact]
    public void CriarPendente_define_estado_inicial()
    {
        var p = FaturaPagamento.CriarPendente(Guid.NewGuid(), "Pix", 50.55m, "EfiPix", Guid.NewGuid(), "txid123", "{\"x\":1}");

        p.Status.Should().Be(StatusFaturaPagamento.Pendente);
        p.Metodo.Should().Be("pix"); // lowercase
        p.Valor.Should().Be(50.55m);
        p.GatewayTransactionId.Should().Be("txid123");
        p.PagoEm.Should().BeNull();
    }

    [Fact]
    public void Confirmar_de_pendente_carimba_pago_em()
    {
        var p = FaturaPagamento.CriarPendente(Guid.NewGuid(), "pix", 10m, "EfiPix", Guid.NewGuid());
        p.Confirmar();
        p.Status.Should().Be(StatusFaturaPagamento.Confirmado);
        p.PagoEm.Should().NotBeNull();
    }

    [Fact]
    public void Confirmar_idempotente()
    {
        var p = FaturaPagamento.CriarConfirmado(Guid.NewGuid(), "pix", 10m, "EfiPix", Guid.NewGuid());
        var act = () => p.Confirmar();
        act.Should().NotThrow();
    }

    [Fact]
    public void SolicitarEstorno_so_a_partir_de_confirmado()
    {
        var p = FaturaPagamento.CriarPendente(Guid.NewGuid(), "pix", 10m, "EfiPix", Guid.NewGuid());
        var act = () => p.SolicitarEstorno();
        act.Should().Throw<RegraDeDominioVioladaException>();

        p.Confirmar();
        p.SolicitarEstorno();
        p.Status.Should().Be(StatusFaturaPagamento.EstornoSolicitado);

        p.ConfirmarEstorno();
        p.Status.Should().Be(StatusFaturaPagamento.Estornado);
    }

    [Fact]
    public void CriarPendente_rejeita_valor_zero_ou_negativo()
    {
        Action act1 = () => FaturaPagamento.CriarPendente(Guid.NewGuid(), "pix", 0, "EfiPix", Guid.NewGuid());
        Action act2 = () => FaturaPagamento.CriarPendente(Guid.NewGuid(), "pix", -1, "EfiPix", Guid.NewGuid());
        act1.Should().Throw<RegraDeDominioVioladaException>();
        act2.Should().Throw<RegraDeDominioVioladaException>();
    }
}

public class FaturaItemTests
{
    [Fact]
    public void Criar_calcula_subtotal_arredondado()
    {
        var item = FaturaItem.Criar(Guid.NewGuid(), "Item", 3m, 33.333m, TipoItemFatura.Servico);
        item.Subtotal.Should().Be(100.00m); // 3 * 33.333 = 99.999 → 100.00
    }

    [Fact]
    public void Criar_desconto_subtotal_negativo()
    {
        var item = FaturaItem.Criar(Guid.NewGuid(), "Cupom", 1m, 10m, TipoItemFatura.Desconto);
        item.Subtotal.Should().Be(-10m);
    }

    [Fact]
    public void Criar_rejeita_descricao_vazia()
    {
        Action act = () => FaturaItem.Criar(Guid.NewGuid(), "  ", 1m, 1m, TipoItemFatura.Servico);
        act.Should().Throw<ArgumentException>();
    }
}
