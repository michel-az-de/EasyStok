using EasyStock.Application.Common;

namespace EasyStock.Application.Tests.Common;

public class HorarioBrasilTests
{
    // 2026-06-10T02:30Z = 2026-06-09 23:30 BRT (janela noturna 21h-23h59).
    private static readonly DateTime NoiteUtc = new(2026, 6, 10, 2, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void DataOperacional_na_janela_noturna_fica_no_dia_de_Brasilia()
    {
        HorarioBrasil.DataOperacional(NoiteUtc).Should().Be(new DateOnly(2026, 6, 9));
    }

    [Fact]
    public void DataOperacional_apos_meia_noite_de_Brasilia_vira_o_dia()
    {
        // 2026-06-10T03:30Z = 2026-06-10 00:30 BRT.
        var depois = new DateTime(2026, 6, 10, 3, 30, 0, DateTimeKind.Utc);
        HorarioBrasil.DataOperacional(depois).Should().Be(new DateOnly(2026, 6, 10));
    }

    [Fact]
    public void JanelaDiaUtc_da_o_intervalo_UTC_real_do_dia_civil_de_Brasilia()
    {
        var (ini, fim) = HorarioBrasil.JanelaDiaUtc(new DateOnly(2026, 6, 9));
        ini.Should().Be(new DateTime(2026, 6, 9, 3, 0, 0, DateTimeKind.Utc));
        fim.Should().Be(new DateTime(2026, 6, 10, 3, 0, 0, DateTimeKind.Utc));
        ini.Kind.Should().Be(DateTimeKind.Utc);
        (fim - ini).Should().Be(TimeSpan.FromHours(24));
    }

    [Fact]
    public void CivilComoInstanteUtc_e_a_meia_noite_UTC_da_data()
    {
        var instante = HorarioBrasil.CivilComoInstanteUtc(new DateOnly(2026, 6, 9));
        instante.Should().Be(new DateTime(2026, 6, 9, 0, 0, 0, DateTimeKind.Utc));
        instante.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void Instante_e_data_civil_diferem_em_3h_e_provam_o_bug_financeiro()
    {
        var dia = new DateOnly(2026, 6, 14);
        var inicioInstante = HorarioBrasil.JanelaDiaUtc(dia).IniUtc;      // 03:00Z (coluna de instante)
        var civilComoInstante = HorarioBrasil.CivilComoInstanteUtc(dia);  // 00:00Z (coluna de data civil)
        (inicioInstante - civilComoInstante).Should().Be(TimeSpan.FromHours(3));

        // Parcela vence 14/06, gravada como DATA CIVIL (meia-noite UTC). No proprio dia 14/06:
        var parcelaVence = HorarioBrasil.CivilComoInstanteUtc(dia);
        // Referencia de DATA CIVIL (HojeInstanteUtc-equivalente): nao vencida no dia do vencimento.
        (parcelaVence < HorarioBrasil.CivilComoInstanteUtc(dia)).Should().BeFalse();
        // Usar a janela de INSTANTE (03:00Z) marcaria vencida no proprio dia: por isso NAO se
        // usa JanelaDiaUtc em coluna de data civil.
        (parcelaVence < inicioInstante).Should().BeTrue();
    }

    [Fact]
    public void Resolver_sem_iana_nem_windows_cai_no_fixo_menos_3_sem_lancar()
    {
        var (tz, fonte) = HorarioBrasil.Resolver(_ => throw new TimeZoneNotFoundException());
        fonte.Should().Be(FonteFuso.FallbackFixo);
        tz.BaseUtcOffset.Should().Be(TimeSpan.FromHours(-3));
    }

    [Fact]
    public void Resolver_so_com_windows_usa_windows()
    {
        var sentinela = TimeZoneInfo.CreateCustomTimeZone("sentinela", TimeSpan.FromHours(-3), "s", "s");
        var (tz, fonte) = HorarioBrasil.Resolver(id =>
            id == "America/Sao_Paulo" ? throw new TimeZoneNotFoundException() : sentinela);
        fonte.Should().Be(FonteFuso.Windows);
        tz.Should().BeSameAs(sentinela);
    }

    [Fact]
    public void ZonaFixaBrasilia_e_menos_3_sem_horario_de_verao()
    {
        var fixo = HorarioBrasil.ZonaFixaBrasilia();
        fixo.BaseUtcOffset.Should().Be(TimeSpan.FromHours(-3));
        fixo.SupportsDaylightSavingTime.Should().BeFalse();
    }

    [Fact]
    public void Ambiente_de_teste_resolve_o_fuso_real_nao_degradado()
    {
        // Canario: CI/dev devem ter tzdata (Linux) ou a zona Windows. Se cair em FallbackFixo,
        // ha um problema de ambiente (imagem sem tzdata) que o startup de producao deve barrar.
        HorarioBrasil.Degradado.Should().BeFalse();
    }

    [Fact]
    public void OffsetMinutosAtual_em_Brasilia_e_menos_180()
    {
        // Brasil sem DST desde 2019: offset fixo -03:00 = -180 min.
        HorarioBrasil.OffsetMinutosAtual().Should().Be(-180);
    }

    [Fact]
    public void ZonaId_nao_e_vazio()
    {
        HorarioBrasil.ZonaId.Should().NotBeNullOrWhiteSpace();
    }
}
