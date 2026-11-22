namespace EasyStock.Domain.Services;

/// <summary>
/// Contagens da loja que dizem se os passos essenciais estão prontos (#2070). Lidas a cada consulta, nunca guardadas.
/// </summary>
/// <param name="HorariosGravados">A loja já salvou o expediente (sem a linha vale o padrão de fábrica).</param>
/// <param name="DiasAbertos">Dias da semana com turno no expediente salvo.</param>
/// <param name="TemVitrine">A vitrine do site existe.</param>
/// <param name="TemLogo">A vitrine tem logo.</param>
/// <param name="ItensVisiveisComPreco">Itens do cardápio publicados com preço.</param>
/// <param name="JanelasAtivas">Janelas de entrega ativas.</param>
/// <param name="ZonasAtivas">Zonas de frete ativas.</param>
/// <param name="RaioConfigurado">Frete por raio completo (cozinha, raio e fator).</param>
/// <param name="EquipeAtivaPorNivel">Pessoas com vínculo e conta ativos, pelo nível do perfil na empresa.</param>
public sealed record RetratoDaConfiguracaoInicial(
    bool HorariosGravados,
    int DiasAbertos,
    bool TemVitrine,
    bool TemLogo,
    int ItensVisiveisComPreco,
    int JanelasAtivas,
    int ZonasAtivas,
    bool RaioConfigurado,
    IReadOnlyDictionary<NivelAcesso, int> EquipeAtivaPorNivel);

/// <summary>Prontidão de um passo essencial: pronto, ou a lista do que falta em PT-BR curto.</summary>
public sealed record ProntidaoDoPasso(bool Ok, IReadOnlyList<string> Falta);

/// <summary>
/// Regra única da prontidão dos passos essenciais da configuração inicial (#2070): função pura sobre o retrato.
/// Passo sem prontidão (boas-vindas, a parte de ajustes e a revisão) devolve nulo.
/// </summary>
public static class ProntidaoDaConfiguracaoInicial
{
    public const string FaltaSalvarHorarios = "Salvar os horários da semana";
    public const string FaltaDiaAberto = "Abrir ao menos um dia na semana";
    public const string FaltaVitrine = "Criar a vitrine do site";
    public const string FaltaLogo = "Enviar o logo da loja";
    public const string FaltaItemComPreco = "Publicar ao menos um item com preço";
    public const string FaltaJanela = "Ativar ao menos uma janela de entrega";
    public const string FaltaFrete = "Cadastrar uma zona de entrega ou o raio";
    public const string FaltaEquipe = "Convidar quem atende ou cozinha";

    public static ProntidaoDoPasso? Avaliar(string passo, RetratoDaConfiguracaoInicial retrato)
    {
        ArgumentNullException.ThrowIfNull(retrato);
        return passo switch
        {
            ConfiguracaoInicial.Horarios => Falta(
                (!retrato.HorariosGravados, FaltaSalvarHorarios),
                (retrato.HorariosGravados && retrato.DiasAbertos == 0, FaltaDiaAberto)),
            ConfiguracaoInicial.SiteEImpressos => Falta((!retrato.TemVitrine, FaltaVitrine), (!retrato.TemLogo, FaltaLogo)),
            ConfiguracaoInicial.Cardapio => Falta((retrato.ItensVisiveisComPreco == 0, FaltaItemComPreco)),
            ConfiguracaoInicial.Janelas => Falta((retrato.JanelasAtivas == 0, FaltaJanela)),
            ConfiguracaoInicial.Frete => Falta((retrato.ZonasAtivas == 0 && !retrato.RaioConfigurado, FaltaFrete)),
            ConfiguracaoInicial.Equipe => Falta((QuemAtendeOuCozinha(retrato) == 0, FaltaEquipe)),
            _ => null,
        };
    }

    /// <summary>Gerência atende e cozinha também; Atendimento e Cozinha são de nível Operador.</summary>
    private static int QuemAtendeOuCozinha(RetratoDaConfiguracaoInicial retrato) =>
        retrato.EquipeAtivaPorNivel.Where(p => p.Key is NivelAcesso.Gerente or NivelAcesso.Operador).Sum(p => p.Value);

    private static ProntidaoDoPasso Falta(params (bool Falta, string Texto)[] condicoes)
    {
        var falta = condicoes.Where(c => c.Falta).Select(c => c.Texto).ToArray();
        return new ProntidaoDoPasso(falta.Length == 0, falta);
    }
}
