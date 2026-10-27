using EasyStock.Domain.Services;

namespace EasyStock.Application.UseCases.PrimeirosPassos;

/// <summary>Se a pessoa consegue mexer no que o passo grava e, quando não, o motivo em PT-BR curto.</summary>
public sealed record DisponibilidadeDoPasso(bool Disponivel, string? Motivo)
{
    public static readonly DisponibilidadeDoPasso Livre = new(true, null);
}

/// <summary>
/// <c>disponivel</c> de cada passo da configuração inicial (#2070), pela mesma regra das <c>acoes</c> de
/// <c>GET api/auth/me/modulos</c>: o console não oferece passo que a rota do passo recusaria. Sem ler banco.
/// </summary>
public static class DisponibilidadeDosPassos
{
    public const string MotivoNivel = "Seu perfil não altera esta parte.";
    public const string MotivoAtenderConversas = "Seu perfil não atende conversas.";

    public static DisponibilidadeDoPasso Avaliar(string passo, ICurrentUserAccessor usuario)
    {
        ArgumentNullException.ThrowIfNull(usuario);
        var gerente = usuario.Nivel is NivelAcesso.SuperAdmin or NivelAcesso.Admin or NivelAcesso.Gerente;
        var administrador = usuario.Nivel is NivelAcesso.SuperAdmin or NivelAcesso.Admin;

        return passo switch
        {
            // editarAtendimento: expediente, configuração do atendimento e caderno do agente.
            ConfiguracaoInicial.Horarios or ConfiguracaoInicial.Atendimento or ConfiguracaoInicial.Agente =>
                Exigir(usuario, administrador, Modulo.Atendimento),
            ConfiguracaoInicial.Cardapio => Exigir(usuario, gerente, Modulo.Cardapio), // editarCardapio
            ConfiguracaoInicial.Producao => Exigir(usuario, gerente, Modulo.Producao), // editarProducao
            ConfiguracaoInicial.Respostas => EditarRespostas(usuario, gerente),
            ConfiguracaoInicial.Janelas or ConfiguracaoInicial.Frete =>
                administrador ? DisponibilidadeDoPasso.Livre : new DisponibilidadeDoPasso(false, MotivoNivel), // ajustarEntrega
            // configurarLoja: vitrine, usuários, lojas e alertas de estoque ficam no módulo Configurações.
            ConfiguracaoInicial.SiteEImpressos or ConfiguracaoInicial.Equipe or ConfiguracaoInicial.DadosDaLoja
                or ConfiguracaoInicial.Estoque => Exigir(usuario, administrador, Modulo.Configuracoes),
            ConfiguracaoInicial.Entregadores => Exigir(usuario, true, Modulo.Entregas),
            // Boas-vindas e revisão não gravam; impressora e avisos valem só no aparelho.
            _ => DisponibilidadeDoPasso.Livre,
        };
    }

    /// <summary>editarRespostas: Gerência para cima, módulo Atendimento e permissão de atender conversas.</summary>
    private static DisponibilidadeDoPasso EditarRespostas(ICurrentUserAccessor usuario, bool gerente)
    {
        var modulo = Exigir(usuario, gerente, Modulo.Atendimento);
        if (!modulo.Disponivel) return modulo;
        return usuario.TemPermissao(Permissao.AtenderConversas)
            ? DisponibilidadeDoPasso.Livre
            : new DisponibilidadeDoPasso(false, MotivoAtenderConversas);
    }

    private static DisponibilidadeDoPasso Exigir(ICurrentUserAccessor usuario, bool nivelBasta, Modulo modulo)
    {
        if (!nivelBasta) return new DisponibilidadeDoPasso(false, MotivoNivel);
        return Liberado(usuario, modulo)
            ? DisponibilidadeDoPasso.Livre
            : new DisponibilidadeDoPasso(false, $"O módulo {NomeDe(modulo)} não está liberado no seu perfil.");
    }

    /// <summary>Mesma regra de <c>modulos[].liberado</c> em <c>me/modulos</c>: empresa no token e permissão do módulo.</summary>
    private static bool Liberado(ICurrentUserAccessor usuario, Modulo modulo) =>
        usuario.EmpresaId != Guid.Empty && usuario.TemPermissao(AcessoModulos.PermissaoDe(modulo));

    private static string NomeDe(Modulo modulo) => modulo switch
    {
        Modulo.Cardapio => "Cardápio",
        Modulo.Producao => "Produção",
        Modulo.Configuracoes => "Configurações",
        _ => modulo.ToString(),
    };
}
