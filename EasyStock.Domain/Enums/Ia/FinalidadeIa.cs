namespace EasyStock.Domain.Enums.Ia;

/// <summary>#2957: para que a chamada de IA foi feita. Separa o uso do mês por finalidade.</summary>
public enum FinalidadeIa
{
    Outra = 0,
    Atendimento = 1,
    AssistenteDona = 2,
    AssistenteCardapio = 3,
    ResumoCliente = 4,
    SugestaoCadastro = 5,
    SugestaoReceita = 6,
    Ficha = 7,
    Descricao = 8,
}
