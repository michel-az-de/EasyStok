using EasyStock.Application.Services.Notifications;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Application.Tests.Services.Notifications;

/// <summary>N5: a rotina da empresa vale antes da global, e a da empresa sem canais não esconde a global.</summary>
public class SeletorRotinaTests
{
    private readonly Guid _empresaId = Guid.NewGuid();

    private static RotinaNotificacao Rotina(Guid? empresaId, string canaisJson)
    {
        var r = RotinaNotificacao.Criar(
            "r", "Rotina", TipoEventoNotificacao.FaturaVencida, TriggerTipoRotina.Evento, "tpl",
            CategoriaConteudoNotificacao.Operacional, empresaId: empresaId);
        r.DefinirFallback(canaisJson, "system");
        return r;
    }

    [Fact]
    public void RotinaDaEmpresaVenceAGlobalNaOrdemQueVier()
    {
        var global = Rotina(null, "[\"Email\"]");
        var daEmpresa = Rotina(_empresaId, "[\"WhatsApp\"]");

        SeletorRotina.Escolher([global, daEmpresa], _empresaId).Should().BeSameAs(daEmpresa);
        SeletorRotina.Escolher([daEmpresa, global], _empresaId).Should().BeSameAs(daEmpresa);
    }

    [Fact]
    public void RotinaDaEmpresaSemCanaisNaoEscondeAGlobal()
    {
        var global = Rotina(null, "[\"Email\"]");
        var semCanais = Rotina(_empresaId, "[]");

        SeletorRotina.Escolher([semCanais, global], _empresaId).Should().BeSameAs(global);
    }

    [Fact]
    public void SemNenhumaComCanaisPrefereADaEmpresa()
    {
        var global = Rotina(null, "[]");
        var semCanais = Rotina(_empresaId, "[]");

        SeletorRotina.Escolher([global, semCanais], _empresaId).Should().BeSameAs(semCanais);
    }

    [Fact]
    public void IgnoraRotinaDeOutraEmpresaESemRotinaDevolveNulo()
    {
        var outra = Rotina(Guid.NewGuid(), "[\"Email\"]");

        SeletorRotina.Escolher([outra], _empresaId).Should().BeNull();
    }
}
