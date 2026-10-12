namespace EasyStock.Domain.Enums.Atendimento;

/// <summary>
/// Empresa do entregador (S44). O cadastro é manual; a integração com 99, Lalamove e iFood Entregas
/// fica fora e vai preencher os mesmos campos.
/// </summary>
public enum EmpresaEntregador
{
    Propria = 1,
    NoveNove = 2,
    Lalamove = 3,
    Ifood = 4,
    Outra = 5
}
