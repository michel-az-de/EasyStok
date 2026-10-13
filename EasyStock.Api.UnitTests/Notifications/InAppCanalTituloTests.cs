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
    [InlineData(CategoriaConteudoNotificacao.Operacional, "Caixa aberto fora do horário", "Caixa aberto fora do horário", SeveridadeNotificacao.Media)]
    [InlineData(CategoriaConteudoNotificacao.Seguranca, "Senha alterada", "Senha alterada", SeveridadeNotificacao.Media)]
    [InlineData(CategoriaConteudoNotificacao.Transacional, "Pedido em preparo", "Pedido em preparo", SeveridadeNotificacao.Informativa)]
    [InlineData(CategoriaConteudoNotificacao.Operacional, "", "Validade Próxima", SeveridadeNotificacao.Media)]
    [InlineData(CategoriaConteudoNotificacao.Operacional, "  ", "Validade Próxima", SeveridadeNotificacao.Media)]
    public async Task PreservaAssuntoRealComFallbackSemMudarSeveridadeOuCorrelacao(
        CategoriaConteudoNotificacao categoria, string assunto, string tituloEsperado, SeveridadeNotificacao severidade)
    {
        var repo = Substitute.For<INotificacaoRepository>();
        var usuarioId = Guid.NewGuid();
        var mensagem = new MensagemPronta(Guid.NewGuid(), Guid.NewGuid(), usuarioId.ToString(), assunto,
            "Confira a abertura do caixa.", CanalNotificacao.InApp, categoria);

        var resultado = await new InAppCanal(repo, NullLogger<InAppCanal>.Instance).EnviarAsync(mensagem);

        resultado.Sucesso.Should().BeTrue();
        await repo.Received(1).AddAsync(Arg.Is<Notificacao>(n => n.Titulo == tituloEsperado
            && n.Mensagem == mensagem.Corpo && n.Severidade == severidade && n.EmpresaId == mensagem.EmpresaId
            && n.UsuarioId == usuarioId && n.OutboxMensagemId == mensagem.OutboxId && n.ReferenciaId == mensagem.OutboxId));
    }
}
