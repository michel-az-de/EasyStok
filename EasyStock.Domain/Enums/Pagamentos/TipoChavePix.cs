namespace EasyStock.Domain.Enums.Pagamentos;

/// <summary>Tipos de chave Pix do DICT (#2233). Gravado como inteiro em <c>lojas.pix_tipo</c>.</summary>
public enum TipoChavePix
{
    Cpf = 1,
    Cnpj = 2,
    Email = 3,
    Telefone = 4,
    Aleatoria = 5,
}
