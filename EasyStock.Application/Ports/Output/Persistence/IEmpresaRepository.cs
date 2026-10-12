namespace EasyStock.Application.Ports.Output.Persistence
{
    public interface IEmpresaRepository
    {
        Task<Empresa?> GetByIdAsync(Guid id);
        Task<Empresa?> GetByDocumentoAsync(string documento);
        /// <summary>Resolve a empresa pelo phone_number_id da Meta — usado pelo webhook (S03) para rotear o evento ao tenant certo.</summary>
        Task<Empresa?> GetByWhatsAppPhoneNumberIdAsync(string phoneNumberId, CancellationToken ct = default);

        /// <summary>S35: empresa da página do Facebook (Messenger) ou da conta do Instagram.</summary>
        Task<Empresa?> GetByFacebookPageIdAsync(string pageId, CancellationToken ct = default);
        Task<Empresa?> GetByInstagramAccountIdAsync(string instagramAccountId, CancellationToken ct = default);
        Task<IEnumerable<Empresa>> GetAllAsync();
        /// <summary>
        /// Itera todas as empresas em streaming, sem carregar tudo em memoria.
        /// Preferir sobre GetAllAsync em background jobs e relatorios.
        /// </summary>
        IAsyncEnumerable<Empresa> StreamAllAsync(CancellationToken ct = default);
        Task AddAsync(Empresa empresa);
        Task UpdateAsync(Empresa empresa);
    }
}
