using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Infra.Notifications.InApp;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Notifications;

public class InAppCanalTituloTests
{
    [Theory]
    [InlineData(CategoriaConteudoNotificacao.Operacional, "Caixa aberto fora do horário", "Caixa aberto fora do horário", SeveridadeNotificacao.Media, TipoAlertaEstoque.AvisoOperacional)]
    [InlineData(CategoriaConteudoNotificacao.Seguranca, "Senha alterada", "Senha alterada", SeveridadeNotificacao.Media, TipoAlertaEstoque.AvisoOperacional)]
    [InlineData(CategoriaConteudoNotificacao.Transacional, "Pedido em preparo", "Pedido em preparo", SeveridadeNotificacao.Informativa, TipoAlertaEstoque.PedidoRecebido)]
    // #2301: aviso operacional sem assunto não vira "Validade Próxima".
    [InlineData(CategoriaConteudoNotificacao.Operacional, "", "Aviso", SeveridadeNotificacao.Media, TipoAlertaEstoque.AvisoOperacional)]
    [InlineData(CategoriaConteudoNotificacao.Operacional, "  ", "Aviso", SeveridadeNotificacao.Media, TipoAlertaEstoque.AvisoOperacional)]
    public async Task PreservaAssuntoRealComFallbackSemMudarSeveridadeOuCorrelacao(
        CategoriaConteudoNotificacao categoria, string assunto, string tituloEsperado, SeveridadeNotificacao severidade,
        TipoAlertaEstoque tipoEsperado)
    {
        var repo = Substitute.For<INotificacaoRepository>();
        var usuarioId = Guid.NewGuid();
        var mensagem = new MensagemPronta(Guid.NewGuid(), Guid.NewGuid(), usuarioId.ToString(), assunto,
            "Confira a abertura do caixa.", CanalNotificacao.InApp, categoria);

        var resultado = await new InAppCanal(repo, NullLogger<InAppCanal>.Instance).EnviarAsync(mensagem);

        resultado.Sucesso.Should().BeTrue();
        await repo.Received(1).AddAsync(Arg.Is<Notificacao>(n => n.Titulo == tituloEsperado && n.TipoAlerta == tipoEsperado
            && n.Mensagem == mensagem.Corpo && n.Severidade == severidade && n.EmpresaId == mensagem.EmpresaId
            && n.UsuarioId == usuarioId && n.OutboxMensagemId == mensagem.OutboxId && n.ReferenciaId == mensagem.OutboxId));
    }
}
