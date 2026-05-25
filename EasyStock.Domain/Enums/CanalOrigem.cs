namespace EasyStock.Domain.Enums
{
    /// <summary>
    /// Canal de origem pelo qual um ticket ou interação foi iniciado.
    /// </summary>
    public enum CanalOrigem
    {
        Admin = 0,
        Pwa = 1,
        Web = 2,
        Mobile = 3,
        Site = 4
    }
}
