namespace EasyStock.Application.Ports.Output.Persistence
{
    /// <summary>
    /// Registro de interações (visto/confirmado) de usuários com banners (#869).
    /// Tabela global sem <c>EmpresaId</c>. Não faz commit — o UnitOfWork persiste.
    /// </summary>
    public interface IBannerConfirmacaoRepository
    {
        /// <summary>
        /// Registra a interação de forma idempotente: se já existe (BannerId, UsuarioId, Tipo),
        /// não duplica e retorna false; caso contrário adiciona e retorna true. A corrida
        /// concorrente é fechada pelo índice único (o chamador trata a violação como sucesso).
        /// </summary>
        Task<bool> RegistrarAsync(Guid bannerId, Guid usuarioId, BannerInteracaoTipo tipo, CancellationToken ct = default);

        /// <summary>Existe alguma confirmação para o banner? Usado no pré-check do DELETE (409).</summary>
        Task<bool> PossuiParaBannerAsync(Guid bannerId, CancellationToken ct = default);

        /// <summary>Contagem de interações de um banner (leitura de verificação/testes).</summary>
        Task<int> ContarAsync(Guid bannerId, CancellationToken ct = default);
    }
}
