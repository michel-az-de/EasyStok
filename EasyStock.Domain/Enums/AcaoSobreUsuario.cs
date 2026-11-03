namespace EasyStock.Domain.Enums
{
    /// <summary>O que alguém da administração faz com o acesso de outra pessoa (#2070). A política de hierarquia decide cada uma.</summary>
    public enum AcaoSobreUsuario
    {
        Criar,
        EditarNome,
        TrocarEmail,
        TrocarPerfil,
        Desativar,
        Reativar,
        ReenviarConvite,
        MarcarDona,
        DesmarcarDona
    }
}
