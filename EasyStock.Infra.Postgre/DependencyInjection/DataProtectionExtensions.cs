using EasyStock.Infra.Postgre.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Infra.Postgre.DependencyInjection
{
    /// <summary>
    /// Registro unico do ASP.NET DataProtection para os hosts de backend (Api e Worker).
    ///
    /// <para>
    /// <b>Por que existe:</b> a Api cifra o certificado A1 (.pfx + senha) e o token do
    /// gateway fiscal em <c>credencial_integracao.payload_cifrado</c>; o Worker decifra
    /// os mesmos bytes ao emitir NFC-e e ao reprocessar contingencia. Sao containers
    /// distintos, entao o ciclo so fecha se os dois lerem o mesmo key ring.
    /// </para>
    ///
    /// <para>
    /// <b>Como:</b> as chaves vivem no proprio Postgres (<c>data_protection_keys</c>), que
    /// Api e Worker ja compartilham. Isso resolve de uma vez os dois motivos pelos quais
    /// o <c>Unprotect</c> cruzado falhava: key rings separados (cada container tinha o seu,
    /// um deles efemero) e application discriminator divergente (nenhum dos dois chamava
    /// <c>SetApplicationName</c>). Tambem sobrevive a recriacao de container e a troca de
    /// host, sem depender de volume montado.
    /// </para>
    ///
    /// <para>
    /// <b>Nao use em Web/Admin:</b> aqueles hosts protegem cookie de sessao e antiforgery
    /// com os nomes proprios <c>EasyStok.Web</c> e <c>EasyStok.Admin</c>. Trocar o
    /// ApplicationName deles invalidaria as sessoes vivas de todos os usuarios.
    /// </para>
    /// </summary>
    public static class DataProtectionExtensions
    {
        /// <summary>
        /// Nome compartilhado por Api e Worker. O ASP.NET deriva a subchave a partir
        /// deste discriminator: se os dois hosts divergirem, o payload cifrado por um
        /// nao e legivel pelo outro mesmo lendo o mesmo key ring.
        /// </summary>
        public const string ApplicationName = "EasyStok.Backend";

        /// <summary>
        /// Requer <see cref="EasyStockDbContext"/> ja registrado no container
        /// (<c>AddEasyStockPostgreInfrastructure</c>).
        /// </summary>
        public static IServiceCollection AddEasyStockDataProtection(this IServiceCollection services)
        {
            services.AddDataProtection()
                .SetApplicationName(ApplicationName)
                .PersistKeysToDbContext<EasyStockDbContext>();

            return services;
        }
    }
}
