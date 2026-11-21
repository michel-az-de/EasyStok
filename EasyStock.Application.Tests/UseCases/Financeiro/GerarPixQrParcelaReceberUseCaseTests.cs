using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Financeiro.Pagamentos;
using EasyStock.Domain.Entities.Financeiro;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.UseCases.Financeiro;

/// <summary>Issue 826: geração do Pix da parcela a receber, sem chamar provedor real.</summary>
public class GerarPixQrParcelaReceberUseCaseTests
{
    private static readonly Guid Empresa = Guid.NewGuid();

    private sealed record Cenario(
        GerarPixQrParcelaReceberUseCase Uc, ParcelaReceber Parcela, IEfiPixService Pix, IUnitOfWork Uow);

    private static Cenario Montar()
    {
        var conta = ContaReceber.Criar(Empresa, clienteId: null, Guid.NewGuid(), "Cliente", DateTime.UtcNow);
        conta.AdicionarParcela(1, 100m, DateTime.UtcNow.AddDays(30));
        conta.Emitir();
        var parcela = conta.Parcelas.First();
        parcela.ContaReceber = conta;

        var contaRepo = Substitute.For<IContaReceberRepository>();
        contaRepo.GetParcelaWithContaAsync(Empresa, parcela.Id, Arg.Any<CancellationToken>()).Returns(parcela);
        var pix = Substitute.For<IEfiPixService>();
        pix.CriarCobrancaAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => new EfiCobrancaResult(ci.ArgAt<string>(0), "copia-e-cola", "qr", DateTime.UtcNow.AddHours(1)));
        var uow = Substitute.For<IUnitOfWork>();
        var uc = new GerarPixQrParcelaReceberUseCase(contaRepo, pix, uow,
            Substitute.For<ILogger<GerarPixQrParcelaReceberUseCase>>());
        return new Cenario(uc, parcela, pix, uow);
    }

    private static GerarPixQrParcelaReceberCommand Cmd(Cenario c) => new(Empresa, c.Parcela.Id);

    [Fact]
    public async Task Gera_cobranca_do_saldo_com_txid_de_conta_a_receber_e_associa_na_parcela()
    {
        var c = Montar();

        var r = await c.Uc.ExecuteAsync(Cmd(c));

        r.Should().NotBeNull();
        r!.Valor.Should().Be(100m);
        r.Txid.Should().StartWith("cr").And.HaveLength(26);
        c.Parcela.EfiTxid.Should().Be(r.Txid);
        await c.Pix.Received(1).CriarCobrancaAsync(r.Txid, 100m, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await c.Uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task Segunda_chamada_com_pix_valido_devolve_o_mesmo_sem_nova_cobranca()
    {
        var c = Montar();

        var primeiro = await c.Uc.ExecuteAsync(Cmd(c));
        var segundo = await c.Uc.ExecuteAsync(Cmd(c));

        segundo!.Txid.Should().Be(primeiro!.Txid);
        await c.Pix.Received(1).CriarCobrancaAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Pix_perto_de_expirar_gera_cobranca_nova()
    {
        var c = Montar();
        c.Parcela.AssociarPix("cr-antigo", "x", "y", DateTime.UtcNow.AddMinutes(2));

        var r = await c.Uc.ExecuteAsync(Cmd(c));

        r!.Txid.Should().NotBe("cr-antigo");
        await c.Pix.Received(1).CriarCobrancaAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Parcela_cancelada_recusa_sem_chamar_o_provedor()
    {
        var c = Montar();
        c.Parcela.Cancelar();

        var act = () => c.Uc.ExecuteAsync(Cmd(c));

        await act.Should().ThrowAsync<UseCaseValidationException>();
        await c.Pix.DidNotReceiveWithAnyArgs().CriarCobrancaAsync(default!, default, default!, default);
    }

    [Fact]
    public async Task Parcela_inexistente_devolve_nulo()
    {
        var c = Montar();

        var r = await c.Uc.ExecuteAsync(new GerarPixQrParcelaReceberCommand(Empresa, Guid.NewGuid()));

        r.Should().BeNull();
    }
}
