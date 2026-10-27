using EasyStock.Api.Controllers;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Financeiro.Pagamentos;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Financeiro;
using EasyStock.Domain.Enums;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Controllers;

// Banca do financeiro (#2095): quem só consulta contas não lança no Caixa (F5, #476) e a baixa manual por Pix
// só vale para a parcela da rota, na empresa de quem chama, com o valor que a Efí confirma (F3).
public class FinanceiroPermissaoEConciliacaoTests
{
    private static readonly Guid Empresa = Guid.NewGuid();

    private static ICurrentUserAccessor SoConsulta(Permissao ver, Permissao gerenciar)
    {
        var user = Substitute.For<ICurrentUserAccessor>();
        user.Nivel.Returns(NivelAcesso.Operador);
        user.EmpresaId.Returns(Empresa);
        user.UsuarioId.Returns(Guid.NewGuid());
        user.TemPermissao(ver).Returns(true);
        user.TemPermissao(gerenciar).Returns(false);
        return user;
    }

    [Fact]
    public async Task Registrar_pagamento_de_conta_a_pagar_exige_gerenciar()
    {
        var repo = Substitute.For<IContaPagarRepository>();
        var caixa = Substitute.For<ICaixaRepository>();
        var conta = ContaPagar.Criar(Empresa, null, Guid.NewGuid(), "Luz", DateTime.UtcNow);
        var parcela = conta.AdicionarParcela(1, 300m, DateTime.UtcNow.AddDays(5));
        conta.Emitir();
        parcela.ContaPagar = conta;
        repo.GetParcelaWithContaAsync(Empresa, parcela.Id, Arg.Any<CancellationToken>()).Returns(parcela);
        var controller = new ContasAPagarController(null!, null!, null!, null!, null!, null!, null!, null!,
            new RegistrarPagamentoParcelaPagarUseCase(repo, caixa, Substitute.For<IUnitOfWork>(), NullLogger<RegistrarPagamentoParcelaPagarUseCase>.Instance),
            null!, SoConsulta(Permissao.VisualizarContasAPagar, Permissao.GerenciarContasAPagar));

        var r = await controller.RegistrarPagamento(conta.Id, parcela.Id,
            new ContasAPagarController.RegistrarPagamentoBody(Empresa, 300m, "dinheiro", null, null, null, null));

        r.Should().BeOfType<ForbidResult>();
        await caixa.DidNotReceive().AddMovimentoAsync(Arg.Any<MovimentoCaixa>());
    }

    [Fact]
    public async Task Registrar_recebimento_de_conta_a_receber_exige_gerenciar()
    {
        var (repo, conta, parcela, _) = ContaReceberComPix(Empresa, "txid-a");
        var caixa = Substitute.For<ICaixaRepository>();
        repo.GetParcelaWithContaAsync(Empresa, parcela.Id, Arg.Any<CancellationToken>()).Returns(parcela);
        var controller = ControllerReceber(repo, caixa, Substitute.For<IEfiPixService>(),
            SoConsulta(Permissao.VisualizarContasAReceber, Permissao.GerenciarContasAReceber));

        var r = await controller.RegistrarPagamento(conta.Id, parcela.Id,
            new ContasAReceberController.RegistrarPagamentoBody(Empresa, 100m, "dinheiro", null, null, null, null));

        r.Should().BeOfType<ForbidResult>();
        await caixa.DidNotReceive().AddMovimentoAsync(Arg.Any<MovimentoCaixa>());
    }

    [Fact]
    public async Task Baixa_manual_por_pix_recusa_txid_de_outra_parcela()
    {
        var (repo, _, parcelaDoTxid, _) = ContaReceberComPix(Empresa, "txid-a");
        var caixa = Substitute.For<ICaixaRepository>();
        var efi = EfiConcluida("txid-a", 100m);
        var controller = ControllerReceber(repo, caixa, efi, Gerente(Empresa));

        var r = await controller.ReconciliarManual(Guid.NewGuid(), new ContasAReceberController.ReconciliarBody("txid-a"), null);

        r.Should().NotBeOfType<OkObjectResult>();
        parcelaDoTxid.Pagamentos.Should().BeEmpty();
        await caixa.DidNotReceive().AddMovimentoAsync(Arg.Any<MovimentoCaixa>());
    }

    [Fact]
    public async Task Baixa_manual_por_pix_recusa_parcela_de_outra_empresa()
    {
        var outraEmpresa = Guid.NewGuid();
        var (repo, _, parcela, _) = ContaReceberComPix(outraEmpresa, "txid-b");
        var caixa = Substitute.For<ICaixaRepository>();
        var controller = ControllerReceber(repo, caixa, EfiConcluida("txid-b", 100m), Gerente(Empresa));

        var r = await controller.ReconciliarManual(parcela.Id, new ContasAReceberController.ReconciliarBody("txid-b"), null);

        r.Should().NotBeOfType<OkObjectResult>();
        parcela.Pagamentos.Should().BeEmpty();
        await caixa.DidNotReceive().AddMovimentoAsync(Arg.Any<MovimentoCaixa>());
    }

    [Fact]
    public async Task Baixa_manual_por_pix_so_baixa_o_que_a_efi_confirma()
    {
        var (repo, _, parcela, _) = ContaReceberComPix(Empresa, "txid-c");
        var caixa = Substitute.For<ICaixaRepository>();
        var efi = Substitute.For<IEfiPixService>();
        efi.ConsultarCobrancaAsync("txid-c", Arg.Any<CancellationToken>())
            .Returns(new EfiCobrancaStatusResult("txid-c", EfiCobrancaStatus.Ativa));
        var controller = ControllerReceber(repo, caixa, efi, Gerente(Empresa));

        await controller.ReconciliarManual(parcela.Id, new ContasAReceberController.ReconciliarBody("txid-c"), null);

        parcela.Pagamentos.Should().BeEmpty("a cobrança ainda está ATIVA na Efí");
        await caixa.DidNotReceive().AddMovimentoAsync(Arg.Any<MovimentoCaixa>());
    }

    [Fact]
    public async Task Baixa_manual_por_pix_da_propria_parcela_confirmada_na_efi_registra()
    {
        var (repo, _, parcela, _) = ContaReceberComPix(Empresa, "txid-d");
        var caixa = Substitute.For<ICaixaRepository>();
        var controller = ControllerReceber(repo, caixa, EfiConcluida("txid-d", 100m), Gerente(Empresa));

        var r = await controller.ReconciliarManual(parcela.Id, new ContasAReceberController.ReconciliarBody("txid-d"), null);

        r.Should().BeOfType<OkObjectResult>();
        parcela.ValorPago.Should().Be(100m);
        await caixa.Received(1).AddMovimentoAsync(Arg.Any<MovimentoCaixa>());
    }

    private static ICurrentUserAccessor Gerente(Guid empresa)
    {
        var user = Substitute.For<ICurrentUserAccessor>();
        user.Nivel.Returns(NivelAcesso.Gerente);
        user.EmpresaId.Returns(empresa);
        user.UsuarioId.Returns(Guid.NewGuid());
        user.TemPermissao(Arg.Any<Permissao>()).Returns(true);
        return user;
    }

    private static IEfiPixService EfiConcluida(string txid, decimal valor)
    {
        var efi = Substitute.For<IEfiPixService>();
        efi.ConsultarCobrancaAsync(txid, Arg.Any<CancellationToken>())
            .Returns(new EfiCobrancaStatusResult(txid, EfiCobrancaStatus.Concluida, valor, valor, null, DateTime.UtcNow));
        return efi;
    }

    private static (IContaReceberRepository Repo, ContaReceber Conta, ParcelaReceber Parcela, string Txid) ContaReceberComPix(Guid empresa, string txid)
    {
        var repo = Substitute.For<IContaReceberRepository>();
        var conta = ContaReceber.Criar(empresa, null, Guid.NewGuid(), "Encomenda", DateTime.UtcNow);
        var parcela = conta.AdicionarParcela(1, 100m, DateTime.UtcNow.AddDays(5));
        conta.Emitir();
        parcela.ContaReceber = conta;
        parcela.AssociarPix(txid, "copia", "qr", DateTime.UtcNow.AddHours(1));
        repo.GetParcelaByEfiTxidAsync(txid, Arg.Any<CancellationToken>()).Returns(parcela);
        repo.GetParcelaWithContaAsync(empresa, parcela.Id, Arg.Any<CancellationToken>()).Returns(parcela);
        return (repo, conta, parcela, txid);
    }

    private static ContasAReceberController ControllerReceber(IContaReceberRepository repo, ICaixaRepository caixa, IEfiPixService efi, ICurrentUserAccessor user)
    {
        var uow = Substitute.For<IUnitOfWork>();
        return new ContasAReceberController(null!, null!, null!, null!, null!, null!, null!, null!,
            new RegistrarPagamentoParcelaReceberUseCase(repo, caixa, uow, NullLogger<RegistrarPagamentoParcelaReceberUseCase>.Instance),
            null!, null!, null!,
            new ReconciliarPixParcelaReceberUseCase(repo, caixa, efi, uow, NullLogger<ReconciliarPixParcelaReceberUseCase>.Instance),
            user);
    }
}
