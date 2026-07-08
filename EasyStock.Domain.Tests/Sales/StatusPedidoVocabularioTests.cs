using EasyStock.Domain.Sales;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Sales;

/// <summary>
/// Fonte única do vocabulário de status (issue 863, Onda 1 do ADR-0042). Garante:
/// cobertura total do enum, o de-para do contrato do cliente preservado (o mesmo que o
/// antigo <c>StatusToContract</c> do storefront) e consistência do bucket com os conjuntos
/// já existentes na <see cref="PedidoStateMachine"/> (sem contradição/duplicação divergente).
/// </summary>
public class StatusPedidoVocabularioTests
{
    public static IEnumerable<object[]> TodosOsStatus()
        => Enum.GetValues<StatusPedido>().Select(s => new object[] { s });

    [Theory]
    [MemberData(nameof(TodosOsStatus))]
    public void De_cobre_todo_o_enum_com_rotulos_nao_vazios(StatusPedido status)
    {
        var p = StatusPedidoVocabulario.De(status);
        p.RotuloLojista.Should().NotBeNullOrWhiteSpace();
        p.ContratoCliente.Should().NotBeNullOrWhiteSpace();
    }

    // De-para do contrato do cliente — congela o comportamento do antigo StatusToContract
    // (aguardando+preparando colapsam em EmPreparo; pronto vira SaiuParaEntrega).
    [Theory]
    [InlineData(StatusPedido.Rascunho, "Rascunho")]
    [InlineData(StatusPedido.AguardandoPagamento, "AguardandoPagamento")]
    [InlineData(StatusPedido.AguardandoAprovacaoBaba, "AguardandoAprovacaoBaba")]
    [InlineData(StatusPedido.AprovadoBaba, "AprovadoBaba")]
    [InlineData(StatusPedido.Aguardando, "EmPreparo")]
    [InlineData(StatusPedido.Preparando, "EmPreparo")]
    [InlineData(StatusPedido.Pronto, "SaiuParaEntrega")]
    [InlineData(StatusPedido.Entregue, "Entregue")]
    [InlineData(StatusPedido.Cancelado, "Cancelado")]
    public void ContratoCliente_preserva_o_de_para_do_storefront(StatusPedido status, string esperado)
        => StatusPedidoVocabulario.ContratoCliente(status).Should().Be(esperado);

    // Unificacao do unico rotulo divergente entre superficies: cockpit/SSR usavam
    // "Em preparo", o StatusHelper do Web usava "Preparando". Canonico = "Em preparo".
    [Fact]
    public void RotuloLojista_de_preparando_e_Em_preparo()
        => StatusPedidoVocabulario.RotuloLojista(StatusPedido.Preparando).Should().Be("Em preparo");

    // O bucket NAO contradiz os conjuntos ja existentes na PedidoStateMachine.
    [Theory]
    [MemberData(nameof(TodosOsStatus))]
    public void Bucket_terminal_bate_com_Finais(StatusPedido status)
        => StatusPedidoVocabulario.EhTerminal(status)
            .Should().Be(PedidoStateMachine.Finais.Contains(status));

    [Theory]
    [MemberData(nameof(TodosOsStatus))]
    public void Bucket_preOperacional_bate_com_PreOperacionais(StatusPedido status)
    {
        var ehPreOp = StatusPedidoVocabulario.Bucket(status) == BucketStatusPedido.PreOperacional;
        ehPreOp.Should().Be(PedidoStateMachine.PreOperacionais.Contains(status));
    }
}
