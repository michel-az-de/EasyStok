namespace EasyStock.Domain.Enums
{
    public enum StatusItemEstoque
    {
        Ok,
        Warn,
        Critical,
        Slow,
        Ativo = Ok,
        Esgotado = Critical,
        Vencido,
        Descartado,
        Bloqueado
    }
}
