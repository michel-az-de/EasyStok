namespace EasyStock.Application.Services.Auth;

/// <summary>
/// #2695 (homologação #1769, U1): a conta interna do sync móvel tem o login travado no cadastro e o vínculo ativo
/// para gravar. Reativar e Desativar pela tela de Usuários não fazem sentido nela: recusam com motivo, em vez de
/// responder sucesso sem efeito ou desligar o sync.
/// </summary>
public static class ContaDeSistema
{
    public const string Codigo = "CONTA_DE_SISTEMA";

    public static void Recusar(Usuario usuario)
    {
        if (usuario.EhContaDeSistema)
            throw new UseCaseValidationException(Codigo,
                "Esta é a conta interna que sincroniza o aplicativo. Ela não se reativa nem se desativa por aqui.");
    }
}
