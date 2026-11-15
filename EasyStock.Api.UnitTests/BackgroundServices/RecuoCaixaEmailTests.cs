using EasyStock.Api.BackgroundServices;
using FluentAssertions;

namespace EasyStock.Api.UnitTests.BackgroundServices;

/// <summary>
/// #1642 (item 3): senha trocada na caixa da loja virava login recusado a cada 60 s para sempre, e e-mail que falha
/// ao gravar voltava no mesmo lote toda rodada, travando a fila.
/// </summary>
public class RecuoCaixaEmailTests
{
    private static readonly DateTime Agora = new(2026, 11, 15, 7, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Intervalo = TimeSpan.FromSeconds(60);
    private readonly Guid _empresa = Guid.NewGuid();
    private readonly RecuoCaixaEmail _recuo = new(Intervalo);

    [Fact]
    public void Falhas_seguidas_dobram_a_espera_ate_o_teto()
    {
        var esperas = Enumerable.Range(0, 8).Select(_ => _recuo.RegistrarFalha(_empresa, Agora)).ToList();

        esperas.Should().Equal(
            TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(8), TimeSpan.FromMinutes(16),
            TimeSpan.FromMinutes(32), RecuoCaixaEmail.Teto, RecuoCaixaEmail.Teto, RecuoCaixaEmail.Teto);
    }

    [Fact]
    public void Caixa_que_falhou_espera_o_recuo_e_as_outras_seguem()
    {
        var outra = Guid.NewGuid();
        var espera = _recuo.RegistrarFalha(_empresa, Agora);

        _recuo.PodeLer(_empresa, Agora.AddSeconds(61)).Should().BeFalse("a rodada seguinte não tenta de novo");
        _recuo.PodeLer(_empresa, Agora + espera).Should().BeTrue();
        _recuo.PodeLer(outra, Agora).Should().BeTrue();
    }

    [Fact]
    public void Leitura_que_deu_certo_zera_o_recuo()
    {
        _recuo.RegistrarFalha(_empresa, Agora);
        _recuo.RegistrarFalha(_empresa, Agora);
        _recuo.RegistrarSucesso(_empresa);

        _recuo.PodeLer(_empresa, Agora).Should().BeTrue();
        _recuo.RegistrarFalha(_empresa, Agora).Should().Be(TimeSpan.FromMinutes(2), "a contagem recomeça");
    }

    [Fact]
    public void Quarentena_guarda_os_ids_por_empresa_e_expira()
    {
        _recuo.Quarentena(_empresa, Agora).Add("42");

        _recuo.Quarentena(_empresa, Agora.AddMinutes(1)).Should().Equal("42");
        _recuo.Quarentena(Guid.NewGuid(), Agora).Should().BeEmpty();
        _recuo.Quarentena(_empresa, Agora + RecuoCaixaEmail.DuracaoQuarentena).Should().BeEmpty("volta a tentar depois");
    }
}
