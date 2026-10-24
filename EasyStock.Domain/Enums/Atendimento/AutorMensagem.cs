namespace EasyStock.Domain.Enums.Atendimento;

public enum AutorMensagem
{
    Cliente = 1,
    Agente = 2,
    /// <summary>A dona (perfil Admin) respondendo pelo console.</summary>
    Dona = 3,
    Sistema = 4,
    /// <summary>#2007: alguém da equipe que não é a dona (gerente, operadora) respondendo pelo console.</summary>
    Atendente = 5
}

public static class AutorMensagemExtensions
{
    /// <summary>Gente da empresa respondendo (dona ou equipe): cala o agente e guarda quem enviou.</summary>
    public static bool EhHumanoDaEmpresa(this AutorMensagem autor) => autor is AutorMensagem.Dona or AutorMensagem.Atendente;
}
