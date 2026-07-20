using EasyStock.Domain.Entities.Financeiro;
using EasyStock.Domain.Enums.Financeiro;
using EasyStock.Domain.Exceptions;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities.Financeiro;

public class ParcelaTests
{
    private static readonly Guid Empresa = Guid.NewGuid();

    [Fact]
    public void ParcelaPagar_pagamento_total_marca_paga()
    {
        var p = ParcelaPagar.Criar(Guid.NewGuid(), Empresa, 1, 100m, DateTime.UtcNow.AddDays(10));
        var pag = PagamentoParcela.CriarConfirmado(Empresa, TipoLadoFinanceiro.Pagar, 100m, "dinheiro", DateTime.UtcNow);
        p.RegistrarPagamento(pag, DateTime.UtcNow);

        p.Status.Should().Be(StatusParcela.Paga);
        p.ValorPago.Should().Be(100m);
        p.DataPagamentoTotal.Should().NotBeNull();
    }

    [Fact]
    public void ParcelaPagar_pagamento_parcial_marca_parcial()
    {
        var p = ParcelaPagar.Criar(Guid.NewGuid(), Empresa, 1, 100m, DateTime.UtcNow.AddDays(10));
        var pag = PagamentoParcela.CriarConfirmado(Empresa, TipoLadoFinanceiro.Pagar, 60m, "dinheiro", DateTime.UtcNow);
        p.RegistrarPagamento(pag, DateTime.UtcNow);

        p.Status.Should().Be(StatusParcela.ParcialmentePaga);
        p.ValorPago.Should().Be(60m);
        p.DataPagamentoTotal.Should().BeNull();
    }

    [Fact]
    public void ParcelaPagar_soma_pagamentos_alem_do_valor_falha()
    {
        var p = ParcelaPagar.Criar(Guid.NewGuid(), Empresa, 1, 100m, DateTime.UtcNow.AddDays(10));
        var pag1 = PagamentoParcela.CriarConfirmado(Empresa, TipoLadoFinanceiro.Pagar, 60m, "dinheiro", DateTime.UtcNow);
        p.RegistrarPagamento(pag1, DateTime.UtcNow);

        var pag2 = PagamentoParcela.CriarConfirmado(Empresa, TipoLadoFinanceiro.Pagar, 50m, "dinheiro", DateTime.UtcNow);
        var act = () => p.RegistrarPagamento(pag2, DateTime.UtcNow);

        act.Should().Throw<RegraDeDominioVioladaException>().WithMessage("*exceder*");
    }

    [Fact]
    public void ParcelaPagar_pagamento_de_outra_empresa_falha()
    {
        var p = ParcelaPagar.Criar(Guid.NewGuid(), Empresa, 1, 100m, DateTime.UtcNow.AddDays(10));
        var pag = PagamentoParcela.CriarConfirmado(Guid.NewGuid(), TipoLadoFinanceiro.Pagar, 100m, "dinheiro", DateTime.UtcNow);
        var act = () => p.RegistrarPagamento(pag, DateTime.UtcNow);
        act.Should().Throw<RegraDeDominioVioladaException>().WithMessage("*empresa*");
    }

    [Fact]
    public void ParcelaPagar_marcar_vencida_quando_data_passou_e_status_pendente()
    {
        var p = ParcelaPagar.Criar(Guid.NewGuid(), Empresa, 1, 100m, DateTime.UtcNow.AddDays(-5));
        p.MarcarVencidaSeAplicavel(DateTime.UtcNow);
        p.Status.Should().Be(StatusParcela.Vencida);
    }

    [Fact]
    public void ParcelaPagar_AtualizarStatusPorPagamentos_nao_vence_3h_cedo_no_fim_do_dia_BRT()
    {
        // Parte de #553 (Fase C, issue #984): parcela vence no dia civil 2026-07-15 (Brasilia). As 21h
        // BRT desse dia o relogio UTC ja marca 2026-07-16T00:00Z; com o antigo UtcNow.Date a parcela era
        // marcada vencida 3h cedo. AtualizarStatusPorPagamentos e chamado a cada RegistrarPagamento.
        var p = ParcelaPagar.Criar(Guid.NewGuid(), Empresa, 1, 100m, new DateTime(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc));

        // Ainda dia civil 15 em Brasilia (HojeInstanteUtc = 2026-07-15T00:00Z), mesmo que o relogio UTC
        // ja seja 16T00:00Z (21h BRT do dia 15). NAO deve vencer.
        p.AtualizarStatusPorPagamentos(new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc));
        p.Status.Should().Be(StatusParcela.Pendente);

        // Brasilia virou o dia 16 (00h BRT = 03h UTC). Agora sim vence.
        p.AtualizarStatusPorPagamentos(new DateTime(2026, 7, 16, 0, 0, 0, DateTimeKind.Utc));
        p.Status.Should().Be(StatusParcela.Vencida);
    }

    [Fact]
    public void ParcelaPagar_cancelar_com_pagamento_confirmado_falha()
    {
        var p = ParcelaPagar.Criar(Guid.NewGuid(), Empresa, 1, 100m, DateTime.UtcNow.AddDays(10));
        var pag = PagamentoParcela.CriarConfirmado(Empresa, TipoLadoFinanceiro.Pagar, 50m, "dinheiro", DateTime.UtcNow);
        p.RegistrarPagamento(pag, DateTime.UtcNow);
        var act = () => p.Cancelar();
        act.Should().Throw<RegraDeDominioVioladaException>().WithMessage("*estorno*");
    }

    [Fact]
    public void ParcelaReceber_associar_pix_em_parcela_pendente_funciona()
    {
        var p = ParcelaReceber.Criar(Guid.NewGuid(), Empresa, 1, 100m, DateTime.UtcNow.AddDays(10));
        p.AssociarPix("txid-abc", "pix-cola", "qrbase64", DateTime.UtcNow.AddHours(1));
        p.EfiTxid.Should().Be("txid-abc");
        p.PixCopiaCola.Should().Be("pix-cola");
        p.PixExpiraEm.Should().NotBeNull();
    }

    [Fact]
    public void ParcelaReceber_associar_pix_em_paga_falha()
    {
        var p = ParcelaReceber.Criar(Guid.NewGuid(), Empresa, 1, 100m, DateTime.UtcNow.AddDays(10));
        var pag = PagamentoParcela.CriarConfirmado(Empresa, TipoLadoFinanceiro.Receber, 100m, "pix", DateTime.UtcNow);
        p.RegistrarPagamento(pag, DateTime.UtcNow);

        var act = () => p.AssociarPix("txid", "cola", "qr", DateTime.UtcNow);
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void ParcelaReceber_limpar_pix_zera_campos()
    {
        var p = ParcelaReceber.Criar(Guid.NewGuid(), Empresa, 1, 100m, DateTime.UtcNow.AddDays(10));
        p.AssociarPix("txid-abc", "cola", "qr", DateTime.UtcNow.AddHours(1));
        p.LimparPix();
        p.EfiTxid.Should().BeNull();
        p.PixCopiaCola.Should().BeNull();
        p.QrCodeBase64.Should().BeNull();
        p.PixExpiraEm.Should().BeNull();
    }

    [Fact]
    public void ParcelaReceber_lado_pagar_falha()
    {
        var p = ParcelaReceber.Criar(Guid.NewGuid(), Empresa, 1, 100m, DateTime.UtcNow.AddDays(10));
        var pag = PagamentoParcela.CriarConfirmado(Empresa, TipoLadoFinanceiro.Pagar, 100m, "pix", DateTime.UtcNow);
        var act = () => p.RegistrarPagamento(pag, DateTime.UtcNow);
        act.Should().Throw<RegraDeDominioVioladaException>().WithMessage("*Receber*");
    }

    [Fact]
    public void ParcelaReceber_AtualizarStatusPorPagamentos_nao_vence_3h_cedo_no_fim_do_dia_BRT()
    {
        // Parte de #553 (Fase C, issue #984): mesma janela critica de ParcelaPagar, espelhada em Receber.
        var p = ParcelaReceber.Criar(Guid.NewGuid(), Empresa, 1, 100m, new DateTime(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc));

        // Ainda dia civil 15 em Brasilia. NAO deve vencer.
        p.AtualizarStatusPorPagamentos(new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc));
        p.Status.Should().Be(StatusParcela.Pendente);

        // Brasilia virou o dia 16. Agora sim vence.
        p.AtualizarStatusPorPagamentos(new DateTime(2026, 7, 16, 0, 0, 0, DateTimeKind.Utc));
        p.Status.Should().Be(StatusParcela.Vencida);
    }

    [Fact]
    public void CarimbarNotificacao_atualiza_campo_correspondente()
    {
        var p = ParcelaPagar.Criar(Guid.NewGuid(), Empresa, 1, 100m, DateTime.UtcNow.AddDays(2));
        var agora = DateTime.UtcNow;
        p.CarimbarNotificacao(TipoEventoContaFinanceira.NotificadaD3, agora);
        p.NotificadaD3Em.Should().Be(agora);
        p.NotificadaD1Em.Should().BeNull();
    }
}
