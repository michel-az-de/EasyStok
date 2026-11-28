using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities.Notifications;

// #3400: o Desfazer do sino volta o aviso ao estado de antes da leitura.
public class NotificacaoLeituraTests
{
    [Fact]
    public void MarcarComoNaoLida_desfaz_a_leitura()
    {
        var aviso = Notificacao.Criar(Guid.NewGuid(), TipoAlertaEstoque.PedidoRecebido, "Aviso de teste");
        aviso.MarcarComoLida();

        aviso.MarcarComoNaoLida();

        aviso.Lida.Should().BeFalse();
        aviso.LidaEm.Should().BeNull();
    }
}
