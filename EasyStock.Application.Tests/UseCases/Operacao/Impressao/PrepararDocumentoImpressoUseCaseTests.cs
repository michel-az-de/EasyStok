using EasyStock.Application.Ports.Output.Persistence.Operacao;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Operacao.Impressao;
using EasyStock.Domain.Entities.Operacao;

namespace EasyStock.Application.Tests.UseCases.Operacao.Impressao;

public class PrepararDocumentoImpressoUseCaseTests
{
    private readonly IImpressaoPendenteRepository repo = Substitute.For<IImpressaoPendenteRepository>();
    private readonly IPedidoRepository pedidos = Substitute.For<IPedidoRepository>();
    private readonly IUnitOfWork uow = Substitute.For<IUnitOfWork>();
    private readonly Guid empresa = Guid.NewGuid();
    private PrepararDocumentoImpressoUseCase Caso => new(repo, pedidos, uow, TimeProvider.System);
    private PrepararDocumentoImpressoInput Entrada(Guid pedido) => new(empresa, pedido, Guid.NewGuid(), Guid.NewGuid(),
        "pedido", "a4", "barras", "HP · folha A4", false, "<html>original</html>", "piloto-1");

    [Fact]
    public async Task Retry_repete_ID_sem_gravar_nem_substituir_conteudo()
    {
        var pedido = Pedido.Criar(empresa, origem: "whatsapp");
        pedidos.GetByIdAsync(empresa, pedido.Id).Returns(pedido);
        var input = Entrada(pedido.Id);
        ImpressaoPendente? salvo = null;
        await repo.AddAsync(Arg.Do<ImpressaoPendente>(i => salvo = i), Arg.Any<CancellationToken>());
        repo.ClearReceivedCalls();
        var primeiro = await Caso.ExecuteAsync(input);
        repo.GetBySolicitacaoAsync(empresa, input.SolicitacaoId, Arg.Any<CancellationToken>()).Returns(salvo);
        var repetido = await Caso.ExecuteAsync(input with { Html = "mudou" });
        repetido.Should().Be(primeiro);
        salvo!.Html.Should().Be(input.Html);
        await uow.Received(1).CommitAsync();
        var outra = () => Caso.ExecuteAsync(input with { Modelo = "a5" });
        await outra.Should().ThrowAsync<UseCaseConcurrencyException>();
    }

    [Fact]
    public async Task Reimpressao_copia_snapshot_hash_e_versao_do_original()
    {
        var pedido = Pedido.Criar(empresa, origem: "whatsapp");
        pedidos.GetByIdAsync(empresa, pedido.Id).Returns(pedido);
        var original = ImpressaoPendente.PrepararDocumento(empresa, null, pedido.Id, Guid.NewGuid(), null,
            "entrega", "a5", "qr", "sistema", "antiga", "original intacto", "hash-antigo", "intencao", DateTime.UtcNow);
        original.MarcarImpressa(DateTime.UtcNow);
        repo.GetByIdAsync(empresa, original.Id, Arg.Any<CancellationToken>()).Returns(original);
        ImpressaoPendente? salvo = null;
        await repo.AddAsync(Arg.Do<ImpressaoPendente>(i => salvo = i), Arg.Any<CancellationToken>());
        var dto = await Caso.ExecuteAsync(Entrada(pedido.Id) with { OriginalId = original.Id });
        dto!.Id.Should().NotBe(original.Id);
        dto.OriginalId.Should().Be(original.Id);
        dto.Versao.Should().Be("antiga");
        dto.Hash.Should().Be("hash-antigo");
        salvo!.Html.Should().Be("original intacto");
        salvo.Status.Should().Be(EasyStock.Domain.Enums.Operacao.StatusImpressao.Preparada);
        original.Status.Should().Be(EasyStock.Domain.Enums.Operacao.StatusImpressao.Impressa);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Original_de_outra_empresa_ou_pedido_nao_e_reimpresso(bool outraEmpresa)
    {
        var pedido = Pedido.Criar(empresa, origem: "whatsapp");
        pedidos.GetByIdAsync(empresa, pedido.Id).Returns(pedido);
        var original = ImpressaoPendente.PrepararDocumento(outraEmpresa ? Guid.NewGuid() : empresa, null,
            outraEmpresa ? pedido.Id : Guid.NewGuid(), Guid.NewGuid(), null, "pedido", "a4", "qr", "sistema", "1", "html", "h", "i", DateTime.UtcNow);
        repo.GetByIdAsync(empresa, original.Id, Arg.Any<CancellationToken>()).Returns(original);
        (await Caso.ExecuteAsync(Entrada(pedido.Id) with { OriginalId = original.Id })).Should().BeNull();
        await uow.DidNotReceive().CommitAsync();
    }
}
