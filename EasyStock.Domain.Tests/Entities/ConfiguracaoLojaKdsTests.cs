using EasyStock.Domain.Entities;

namespace EasyStock.Domain.Tests.Entities;

/// <summary>
/// Flag KdsHabilitado em ConfiguracaoLoja (ADR-0032, fatia 3): default off,
/// liga/preserva via Atualizar (param opcional) e zera no ResetarPadrao.
/// </summary>
public class ConfiguracaoLojaKdsTests
{
    private static ConfiguracaoLoja Padrao() => ConfiguracaoLoja.CriarPadrao(Guid.NewGuid());

    [Fact]
    public void Padrao_tem_kds_desabilitado()
    {
        Assert.False(Padrao().KdsHabilitado);
    }

    [Fact]
    public void Atualizar_liga_kds()
    {
        var c = Padrao();
        c.Atualizar(null, null, null, null, null, null, null, null, null, null, kdsHabilitado: true);
        Assert.True(c.KdsHabilitado);
    }

    [Fact]
    public void Atualizar_sem_kds_preserva_valor()
    {
        var c = Padrao();
        c.Atualizar(null, null, null, null, null, null, null, null, null, null, kdsHabilitado: true);
        c.Atualizar(10, null, null, null, null, null, null, null, null, null); // kds omitido
        Assert.True(c.KdsHabilitado);          // nao foi tocado
        Assert.Equal(10, c.DiasAlertaValidade); // o resto atualizou normal
    }

    [Fact]
    public void ResetarPadrao_desliga_kds()
    {
        var c = Padrao();
        c.Atualizar(null, null, null, null, null, null, null, null, null, null, kdsHabilitado: true);
        c.ResetarPadrao();
        Assert.False(c.KdsHabilitado);
    }
}
