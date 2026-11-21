using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.ReativarLoja;
using Microsoft.Extensions.Logging;
using LojaEntity = EasyStock.Domain.Entities.Loja;

namespace EasyStock.Application.Tests.UseCases;

/// <summary>
/// #2562: reativar loja respeita o limite de lojas do plano, como criar. Sem isso bastava
/// inativar e reativar para passar do limite (a contagem só considera lojas ativas).
/// </summary>
public class ReativarLojaUseCaseTests
{
    private readonly ILojaRepository _lojas = Substitute.For<ILojaRepository>();
    private readonly IAssinaturaEmpresaRepository _assinaturas = Substitute.For<IAssinaturaEmpresaRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly Guid _empresaId = Guid.NewGuid();

    private ReativarLojaUseCase Criar() =>
        new(_lojas, _assinaturas, _uow, Substitute.For<ILogger<ReativarLojaUseCase>>());

    private LojaEntity LojaInativa()
    {
        var loja = LojaEntity.Criar(_empresaId, "Filial");
        loja.Desativar();
        _lojas.GetByIdAsync(loja.Id).Returns(loja);
        return loja;
    }

    private void PlanoComLimite(int limite, int ativas)
    {
        _assinaturas.GetAtivaAsync(_empresaId).Returns(new AssinaturaEmpresa { Plano = new Plano { Nome = "Starter", LimiteLojas = limite } });
        _lojas.CountByEmpresaAsync(_empresaId).Returns(ativas);
    }

    [Fact]
    public async Task Reativar_com_o_limite_de_lojas_atingido_recusa_e_nao_grava()
    {
        var loja = LojaInativa();
        PlanoComLimite(limite: 1, ativas: 1);

        var acao = () => Criar().ExecuteAsync(new ReativarLojaCommand(loja.Id, _empresaId));

        await acao.Should().ThrowAsync<PlanoLimiteAtingidoException>();
        loja.Ativa.Should().BeFalse();
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task Reativar_dentro_do_limite_reativa()
    {
        var loja = LojaInativa();
        PlanoComLimite(limite: 2, ativas: 1);

        await Criar().ExecuteAsync(new ReativarLojaCommand(loja.Id, _empresaId));

        loja.Ativa.Should().BeTrue();
        await _uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task Reativar_loja_ja_ativa_nao_confere_o_limite()
    {
        var loja = LojaEntity.Criar(_empresaId, "Matriz");
        _lojas.GetByIdAsync(loja.Id).Returns(loja);
        PlanoComLimite(limite: 1, ativas: 1);

        await Criar().ExecuteAsync(new ReativarLojaCommand(loja.Id, _empresaId));

        loja.Ativa.Should().BeTrue();
    }
}
