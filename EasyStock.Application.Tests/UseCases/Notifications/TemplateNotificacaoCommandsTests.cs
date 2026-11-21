using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Notifications;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Application.Tests.UseCases.Notifications;

public class TemplateNotificacaoCommandsTests
{
    private readonly Guid _empresa = Guid.NewGuid();
    private readonly ITemplateRepository _repo = Substitute.For<ITemplateRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly List<TemplateNotificacao> _familia = [];
    private bool _transacao;

    public TemplateNotificacaoCommandsTests()
    {
        _repo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(c => _familia.Find(t => t.Id == c.Arg<Guid>()));
        _repo.ListarFamiliaComLockAsync(_empresa, "pedido", Arg.Any<CancellationToken>()).Returns(_ =>
        {
            _transacao.Should().BeTrue("o lock precisa durar até gravar a troca de versão");
            return (IReadOnlyList<TemplateNotificacao>)_familia;
        });
        _repo.AddAsync(Arg.Any<TemplateNotificacao>(), Arg.Any<CancellationToken>())
            .Returns(c => { _familia.Add(c.Arg<TemplateNotificacao>()); return Task.CompletedTask; });
        _uow.ExecuteInTransactionSemRetryAsync(Arg.Any<Func<CancellationToken, Task<TemplateResult>>>(), Arg.Any<CancellationToken>())
            .Returns(async c => { _transacao = true; try { return await c.Arg<Func<CancellationToken, Task<TemplateResult>>>()(default); } finally { _transacao = false; } });
        _uow.ExecuteInTransactionSemRetryAsync(Arg.Any<Func<CancellationToken, Task<AprovarTemplateResult>>>(), Arg.Any<CancellationToken>())
            .Returns(async c => { _transacao = true; try { return await c.Arg<Func<CancellationToken, Task<AprovarTemplateResult>>>()(default); } finally { _transacao = false; } });
        _uow.CommitAsync().Returns(_ => { _transacao.Should().BeTrue(); return 1; });
    }

    private TemplateNotificacao Publicada()
    {
        var t = TemplateNotificacao.Criar("pedido", "Pedido", CanalNotificacao.Email,
            TipoEventoNotificacao.TarefaPendente, "Original", "Corpo original", _empresa);
        t.DefinirMetadados("{\"idioma\":\"pt_BR\"}");
        t.Aprovar("admin");
        t.Ativar();
        _familia.Add(t);
        return t;
    }

    private Task<TemplateResult> Editar(Guid id) =>
        new AtualizarTemplateUseCase(_repo, _uow, NullLogger<AtualizarTemplateUseCase>.Instance)
            .ExecuteAsync(new(id, "Novo", "Corpo novo", "admin", _empresa));

    private Task<AprovarTemplateResult> Aprovar(Guid id) =>
        new AprovarTemplateUseCase(_repo, _uow).ExecuteAsync(new(id, "admin", _empresa));

    [Fact]
    public async Task Editar_preserva_publicada_e_aprovar_troca_a_versao_atomica()
    {
        var publicada = Publicada();
        var criada = await Editar(publicada.Id);
        publicada.Ativo.Should().BeTrue();
        var nova = _familia.Single(t => t.Id == criada.Id);
        nova.Ativo.Should().BeFalse();
        nova.Aprovado.Should().BeFalse();
        nova.Versao.Should().Be(2);
        nova.MetadadosJson.Should().Be(publicada.MetadadosJson);

        await Aprovar(nova.Id);
        publicada.Ativo.Should().BeFalse();
        nova.Aprovado.Should().BeTrue();
        _familia.Where(t => t.Ativo).Should().ContainSingle().Which.Id.Should().Be(nova.Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Versao_antiga_nao_pode_ser_editada_ou_reaprovada(bool aprovar)
    {
        var original = Publicada();
        await Editar(original.Id);
        _uow.ClearReceivedCalls();
        Func<Task> acao = async () => { if (aprovar) await Aprovar(original.Id); else await Editar(original.Id); };
        await acao.Should().ThrowAsync<InvalidOperationException>();
        _familia.Should().HaveCount(2);
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task Editar_rascunho_preserva_publicada_e_avanca_versao()
    {
        var original = Publicada();
        var rascunho = await Editar(original.Id);
        var revisao = await Editar(rascunho.Id);
        revisao.Versao.Should().Be(3);
        original.Ativo.Should().BeTrue();
        await Aprovar(revisao.Id);
        _familia.Where(t => t.Ativo).Should().ContainSingle().Which.Id.Should().Be(revisao.Id);
    }

    [Fact]
    public async Task Criar_codigo_existente_nao_abre_familia_duplicada()
    {
        Publicada();
        var useCase = new CriarTemplateUseCase(_repo, _uow, NullLogger<CriarTemplateUseCase>.Instance);
        Func<Task> acao = () => useCase.ExecuteAsync(new("pedido", "Pedido", CanalNotificacao.Email,
            TipoEventoNotificacao.TarefaPendente, "Assunto", "Corpo", "pt-BR", "admin", _empresa));
        await acao.Should().ThrowAsync<InvalidOperationException>();
        _familia.Should().ContainSingle();
        await _uow.DidNotReceive().CommitAsync();
    }
}
