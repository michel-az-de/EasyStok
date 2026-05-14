using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.Etiquetas;
using Microsoft.EntityFrameworkCore;
using NSubstitute.ExceptionExtensions;

namespace EasyStock.Application.Tests.UseCases;

/// <summary>
/// Testes de F6 para os use cases de etiquetas:
/// - MarcarEtiquetasImpressasUseCase (idempotência, snapshot, audit, cross-tenant)
/// - AtualizarTemplateUseCase (409 concorrência)
/// - MontarPayloadRenderUseCase (ficha completa, sem ficha, fallback)
/// </summary>
public class EtiquetasUseCasesTests
{
    // ── Fixtures comuns ─────────────────────────────────────────────────────
    private static readonly Guid EmpId    = Guid.NewGuid();
    private static readonly Guid LoteId   = Guid.NewGuid();
    private static readonly Guid TplId    = Guid.NewGuid();
    private static readonly Guid OperId   = Guid.NewGuid();
    // Layout mínimo válido: 1 texto + 1 QR dentro dos bounds, sem variável inválida
    private static readonly string Layout = """{"v":1,"size":{"w_mm":80,"h_mm":40,"orientation":"horizontal"},"elements":[{"id":"t1","type":"text","content":"{produto.nome}","font":"sans","size_pt":10,"weight":400,"align":"left","overflow":"clip","x_mm":0,"y_mm":0,"w_mm":40,"h_mm":8},{"id":"qr1","type":"code","content":"{etiqueta.codigo}","format":"qr","x_mm":60,"y_mm":2,"w_mm":18,"h_mm":18}]}""";

    private static MarcarEtiquetasImpressasCommand CmdMarcar(
        IReadOnlyList<Guid> ids,
        string status = "impressa",
        bool overwrite = false) =>
        new(EmpId, LoteId, ids, Layout,
            new LayoutSnapshotMetaDto("Sistema", TplId, "Identificação"),
            status, overwrite, OperId, "127.0.0.1", "test");

    private static LoteEtiqueta Etiqueta(Guid id, string? snapshotMeta = null) =>
        new()
        {
            Id = id,
            Sequencial = 1,
            Codigo = $"ETQ-{id:N}",
            Status = LoteEtiquetaStatus.Pendente,
            LayoutSnapshotJson = snapshotMeta != null ? Layout : null,
            LayoutSnapshotMeta = snapshotMeta,
        };

    private static string MetaComTpl(Guid tplId) =>
        $@"{{""id"":""{tplId}"",""origem"":""Sistema"",""nome"":""Teste""}}";

    // ════════════════════════════════════════════════════════════════════════
    // MarcarEtiquetasImpressasUseCase
    // ════════════════════════════════════════════════════════════════════════

    private MarcarEtiquetasImpressasUseCase BuildMarcar(
        ILoteRepository? lote = null,
        IAuditLogRepository? audit = null,
        IUnitOfWork? uow = null)
    {
        lote  ??= Substitute.For<ILoteRepository>();
        audit ??= Substitute.For<IAuditLogRepository>();
        uow   ??= Substitute.For<IUnitOfWork>();
        return new MarcarEtiquetasImpressasUseCase(lote, audit, uow);
    }

    [Fact]
    public async Task Marcar_EmpresaIdVazio_LancaValidation()
    {
        var uc  = BuildMarcar();
        var cmd = CmdMarcar([Guid.NewGuid()]) with { EmpresaId = Guid.Empty };
        await Assert.ThrowsAsync<UseCaseValidationException>(() => uc.ExecuteAsync(cmd));
    }

    [Fact]
    public async Task Marcar_StatusInvalido_LancaValidation()
    {
        var uc  = BuildMarcar();
        var cmd = CmdMarcar([Guid.NewGuid()], status: "cancelada");
        await Assert.ThrowsAsync<UseCaseValidationException>(() => uc.ExecuteAsync(cmd));
    }

    [Fact]
    public async Task Marcar_SemSnapshot_GravaSnapshotEStatus()
    {
        var etqId = Guid.NewGuid();
        var etq   = Etiqueta(etqId); // sem snapshot
        var lote  = Substitute.For<ILoteRepository>();
        lote.GetEtiquetasForRenderAsync(EmpId, LoteId).Returns([etq]);
        var audit = Substitute.For<IAuditLogRepository>();
        var uow   = Substitute.For<IUnitOfWork>();

        var uc  = BuildMarcar(lote, audit, uow);
        var res = await uc.ExecuteAsync(CmdMarcar([etqId]));

        res.Atualizadas.Should().Be(1);
        res.IgnoradasSnapshotDivergente.Should().Be(0);
        await lote.Received(1).UpdateEtiquetasSnapshotAsync(
            Arg.Is<IEnumerable<Guid>>(ids => ids.Contains(etqId)),
            Arg.Any<string>(), Arg.Any<string>(), "impressa");
        await uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task Marcar_SnapshotIgual_ApenasAtualizaStatus_NaoGravaSnapshot()
    {
        var etqId = Guid.NewGuid();
        var etq   = Etiqueta(etqId, MetaComTpl(TplId)); // snapshot com mesmo TplId
        var lote  = Substitute.For<ILoteRepository>();
        lote.GetEtiquetasForRenderAsync(EmpId, LoteId).Returns([etq]);
        var uow = Substitute.For<IUnitOfWork>();

        var uc  = BuildMarcar(lote, uow: uow);
        var res = await uc.ExecuteAsync(CmdMarcar([etqId]));

        res.Atualizadas.Should().Be(1);
        await lote.Received(1).UpdateEtiquetasStatusAsync(
            Arg.Is<IEnumerable<Guid>>(ids => ids.Contains(etqId)),
            "impressa");
        await lote.DidNotReceive().UpdateEtiquetasSnapshotAsync(
            Arg.Any<IEnumerable<Guid>>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Marcar_SnapshotDivergente_SemOverwrite_Ignora()
    {
        var outroTplId = Guid.NewGuid();
        var etqId = Guid.NewGuid();
        var etq   = Etiqueta(etqId, MetaComTpl(outroTplId)); // snapshot com OUTRO template
        var lote  = Substitute.For<ILoteRepository>();
        lote.GetEtiquetasForRenderAsync(EmpId, LoteId).Returns([etq]);
        var uow = Substitute.For<IUnitOfWork>();

        var uc  = BuildMarcar(lote, uow: uow);
        var res = await uc.ExecuteAsync(CmdMarcar([etqId], overwrite: false));

        res.IgnoradasSnapshotDivergente.Should().Be(1);
        res.Atualizadas.Should().Be(0);
        await lote.DidNotReceive().UpdateEtiquetasSnapshotAsync(
            Arg.Any<IEnumerable<Guid>>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Marcar_SnapshotDivergente_ComOverwrite_GravaEAudit()
    {
        var outroTplId = Guid.NewGuid();
        var etqId = Guid.NewGuid();
        var etq   = Etiqueta(etqId, MetaComTpl(outroTplId));
        var lote  = Substitute.For<ILoteRepository>();
        lote.GetEtiquetasForRenderAsync(EmpId, LoteId).Returns([etq]);
        var audit = Substitute.For<IAuditLogRepository>();
        var uow   = Substitute.For<IUnitOfWork>();

        var uc  = BuildMarcar(lote, audit, uow);
        var res = await uc.ExecuteAsync(CmdMarcar([etqId], overwrite: true));

        res.Atualizadas.Should().Be(1);
        res.IgnoradasSnapshotDivergente.Should().Be(0);
        await lote.Received(1).UpdateEtiquetasSnapshotAsync(
            Arg.Is<IEnumerable<Guid>>(ids => ids.Contains(etqId)),
            Arg.Any<string>(), Arg.Any<string>(), "impressa");
        await audit.Received(1).AddAsync(
            Arg.Is<EasyStock.Domain.Entities.AuditLog>(a =>
                a.Acao == "etiquetas.reimpressas-modelo-diferente"));
    }

    [Fact]
    public async Task Marcar_StatusImpressaEComOperador_GravaAuditImpressas()
    {
        var etqId = Guid.NewGuid();
        var etq   = Etiqueta(etqId);
        var lote  = Substitute.For<ILoteRepository>();
        lote.GetEtiquetasForRenderAsync(EmpId, LoteId).Returns([etq]);
        var audit = Substitute.For<IAuditLogRepository>();
        var uow   = Substitute.For<IUnitOfWork>();

        var uc = BuildMarcar(lote, audit, uow);
        await uc.ExecuteAsync(CmdMarcar([etqId], status: "impressa"));

        await audit.Received().AddAsync(
            Arg.Is<EasyStock.Domain.Entities.AuditLog>(a => a.Acao == "etiquetas.impressas"));
    }

    [Fact]
    public async Task Marcar_RevertePendente_AtualizaSoStatus()
    {
        var etqId = Guid.NewGuid();
        var etq   = Etiqueta(etqId, MetaComTpl(TplId));
        var lote  = Substitute.For<ILoteRepository>();
        lote.GetEtiquetasForRenderAsync(EmpId, LoteId).Returns([etq]);
        var uow = Substitute.For<IUnitOfWork>();

        var uc  = BuildMarcar(lote, uow: uow);
        var res = await uc.ExecuteAsync(CmdMarcar([etqId], status: "pendente"));

        res.Atualizadas.Should().Be(1);
        await lote.Received(1).UpdateEtiquetasStatusAsync(
            Arg.Any<IEnumerable<Guid>>(), "pendente");
    }

    [Fact]
    public async Task Marcar_CrossTenant_EtiquetaDeOutroLoteIgnorada()
    {
        // Se a etiqueta não está no lote consultado, o repo retorna lista vazia
        var lote = Substitute.For<ILoteRepository>();
        lote.GetEtiquetasForRenderAsync(EmpId, LoteId).Returns([]);
        var uow = Substitute.For<IUnitOfWork>();

        var uc  = BuildMarcar(lote, uow: uow);
        var res = await uc.ExecuteAsync(CmdMarcar([Guid.NewGuid()]));

        res.Atualizadas.Should().Be(0);
        await lote.DidNotReceive().UpdateEtiquetasSnapshotAsync(
            Arg.Any<IEnumerable<Guid>>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    // ════════════════════════════════════════════════════════════════════════
    // AtualizarTemplateUseCase — concorrência 409
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task AtualizarTemplate_ConflitoConcorrencia_LancaConcurrencyException()
    {
        var tpl = new EasyStock.Domain.Entities.EtiquetaTemplate
        {
            Id = TplId, EmpresaId = EmpId, Nome = "Teste", LayoutJson = Layout
        };
        var repo  = Substitute.For<IEtiquetaTemplateRepository>();
        var audit = Substitute.For<IAuditLogRepository>();
        var uow   = Substitute.For<IUnitOfWork>();

        repo.GetEmpresaByIdAsync(EmpId, TplId).Returns(tpl);
        // Simula conflito de concorrência no commit
        repo.UpdateEmpresaAsync(Arg.Any<EasyStock.Domain.Entities.EtiquetaTemplate>())
            .ThrowsAsync(new DbUpdateConcurrencyException("conflito"));

        var uc  = new AtualizarTemplateUseCase(repo, audit, uow);
        var cmd = new AtualizarTemplateCommand(EmpId, TplId, "Novo Nome", Layout, OperId, null, null);

        await Assert.ThrowsAsync<UseCaseConcurrencyException>(() => uc.ExecuteAsync(cmd));
    }

    [Fact]
    public async Task AtualizarTemplate_TemplateNaoEncontrado_RetornaNull()
    {
        var repo  = Substitute.For<IEtiquetaTemplateRepository>();
        var audit = Substitute.For<IAuditLogRepository>();
        var uow   = Substitute.For<IUnitOfWork>();
        repo.GetEmpresaByIdAsync(EmpId, TplId).Returns((EasyStock.Domain.Entities.EtiquetaTemplate?)null);

        var uc  = new AtualizarTemplateUseCase(repo, audit, uow);
        var cmd = new AtualizarTemplateCommand(EmpId, TplId, "Nome", Layout, null, null, null);

        var res = await uc.ExecuteAsync(cmd);
        res.Should().BeNull();
    }

    [Fact]
    public async Task AtualizarTemplate_LayoutJsonInvalido_LancaValidation()
    {
        var tpl = new EasyStock.Domain.Entities.EtiquetaTemplate
        {
            Id = TplId, EmpresaId = EmpId, Nome = "Teste", LayoutJson = Layout
        };
        var repo  = Substitute.For<IEtiquetaTemplateRepository>();
        var audit = Substitute.For<IAuditLogRepository>();
        var uow   = Substitute.For<IUnitOfWork>();
        repo.GetEmpresaByIdAsync(EmpId, TplId).Returns(tpl);

        var layoutInvalido = """{"v":1,"size":{"w_mm":80,"h_mm":40,"orientation":"horizontal"},"elements":[{"id":"","type":"text","content":"ok","x_mm":0,"y_mm":0,"w_mm":10,"h_mm":5}]}""";

        var uc  = new AtualizarTemplateUseCase(repo, audit, uow);
        var cmd = new AtualizarTemplateCommand(EmpId, TplId, "Nome", layoutInvalido, null, null, null);

        await Assert.ThrowsAsync<UseCaseValidationException>(() => uc.ExecuteAsync(cmd));
    }

    // ════════════════════════════════════════════════════════════════════════
    // MontarPayloadRenderUseCase
    // ════════════════════════════════════════════════════════════════════════

    private MontarPayloadRenderUseCase BuildRender(
        IEtiquetaTemplateRepository? tplRepo = null,
        ILoteRepository? loteRepo = null,
        ILojaRepository? lojaRepo = null)
    {
        tplRepo  ??= Substitute.For<IEtiquetaTemplateRepository>();
        loteRepo ??= Substitute.For<ILoteRepository>();
        lojaRepo ??= Substitute.For<ILojaRepository>();
        return new MontarPayloadRenderUseCase(tplRepo, loteRepo, lojaRepo);
    }

    [Fact]
    public async Task Render_EmpresaIdVazio_LancaValidation()
    {
        var uc = BuildRender();
        await Assert.ThrowsAsync<UseCaseValidationException>(() =>
            uc.ExecuteAsync(new MontarPayloadRenderQuery(Guid.Empty, LoteId, null, null)));
    }

    [Fact]
    public async Task Render_SemEtiquetas_RetornaNull()
    {
        var loteRepo = Substitute.For<ILoteRepository>();
        loteRepo.GetEtiquetasForRenderAsync(EmpId, LoteId).Returns([]);

        var uc  = BuildRender(loteRepo: loteRepo);
        var res = await uc.ExecuteAsync(new MontarPayloadRenderQuery(EmpId, LoteId, null, null));

        res.Should().BeNull();
    }

    [Fact]
    public async Task Render_ProdutoSemFicha_IncluiNaListaSemFicha()
    {
        var prodId = Guid.NewGuid();
        var prod   = new EasyStock.Domain.Entities.Produto { Id = prodId, Nome = "Pão", Marca = "M", AtributosJson = null };
        var item   = new EasyStock.Domain.Entities.LoteItem { Produto = prod, Emoji = "🍞", Unidade = "un" };
        var etq    = new LoteEtiqueta
        {
            Id = Guid.NewGuid(), Sequencial = 1, Codigo = "ETQ-001",
            Status = LoteEtiquetaStatus.Pendente, LoteItem = item,
            Lote = new Lote { Codigo = "LOT-001" }
        };

        var sistemaTemplate = new EasyStock.Domain.Entities.EtiquetaTemplateSistema
        {
            Id = Guid.NewGuid(), LayoutJson = Layout, Ordem = 0
        };

        var loteRepo = Substitute.For<ILoteRepository>();
        loteRepo.GetEtiquetasForRenderAsync(EmpId, LoteId).Returns([etq]);

        var tplRepo = Substitute.For<IEtiquetaTemplateRepository>();
        tplRepo.GetDefaultAsync(EmpId).Returns((EasyStock.Domain.Entities.EtiquetaEmpresaDefault?)null);
        tplRepo.ListSistemaAsync().Returns([sistemaTemplate]);

        var lojaRepo = Substitute.For<ILojaRepository>();
        lojaRepo.GetByEmpresaAsync(EmpId).Returns([]);

        var uc  = BuildRender(tplRepo, loteRepo, lojaRepo);
        var res = await uc.ExecuteAsync(new MontarPayloadRenderQuery(EmpId, LoteId, null, null));

        res.Should().NotBeNull();
        res!.ProdutosSemFicha.Should().Contain(prodId);
    }

    [Fact]
    public async Task Render_FallbackParaPrimeiroSistema_QuandoSemDefault()
    {
        var prodId = Guid.NewGuid();
        var prod   = new EasyStock.Domain.Entities.Produto { Id = prodId, Nome = "Caldo", Marca = "M", AtributosJson = null };
        var item   = new EasyStock.Domain.Entities.LoteItem { Produto = prod };
        var etq    = new LoteEtiqueta
        {
            Id = Guid.NewGuid(), Sequencial = 1, Codigo = "ETQ-002",
            Status = LoteEtiquetaStatus.Pendente, LoteItem = item,
            Lote = new Lote { Codigo = "LOT-002" }
        };

        var sistemaTemplate = new EasyStock.Domain.Entities.EtiquetaTemplateSistema
        {
            Id = TplId, LayoutJson = Layout, Ordem = 0, Nome = "Identificação"
        };

        var loteRepo = Substitute.For<ILoteRepository>();
        loteRepo.GetEtiquetasForRenderAsync(EmpId, LoteId).Returns([etq]);

        var tplRepo = Substitute.For<IEtiquetaTemplateRepository>();
        tplRepo.GetDefaultAsync(EmpId).Returns((EasyStock.Domain.Entities.EtiquetaEmpresaDefault?)null);
        tplRepo.ListSistemaAsync().Returns([sistemaTemplate]);

        var lojaRepo = Substitute.For<ILojaRepository>();
        lojaRepo.GetByEmpresaAsync(EmpId).Returns([]);

        var uc  = BuildRender(tplRepo, loteRepo, lojaRepo);
        var res = await uc.ExecuteAsync(new MontarPayloadRenderQuery(EmpId, LoteId, null, null));

        res.Should().NotBeNull();
        res!.LayoutJson.Should().Be(Layout);
        res.Etiquetas.Should().HaveCount(1);
    }
}
