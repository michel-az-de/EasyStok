namespace EasyStock.Domain.Enums.Atendimento;

/// <summary>S67 (#2534): como terminou um turno do Assistente que chamou o modelo.</summary>
public enum DesfechoTurnoAgente
{
    /// <summary>O robô respondeu ao cliente (texto, ferramenta que enviou ou despedida).</summary>
    Respondeu = 1,

    /// <summary>A conversa passou para a dona ou para a equipe.</summary>
    Escalou = 2,

    /// <summary>Cota mensal esgotada: o turno não chamou o modelo (S69).</summary>
    Cota = 3,

    /// <summary>A chamada ao modelo falhou.</summary>
    Erro = 4,

    /// <summary>A conversa mudou durante o turno (a dona assumiu ou o cliente foi corrigido) e nada saiu.</summary>
    Interrompido = 5,
}
