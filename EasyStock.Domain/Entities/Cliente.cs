using System;
using System.Collections.Generic;

namespace EasyStock.Domain.Entities
{
    /// <summary>
    /// Cliente do ERP (Onda P1). Estrutura **expansível** semelhante a
    /// <see cref="Produto"/>: campos primários direto na entidade raiz pra
    /// UX rápida + tabelas auxiliares 1:N pra crescer (endereços, telefones,
    /// documentos, alterações).
    ///
    /// Os campos primários (Apt/Endereco/Telefone/Email/Documento) refletem
    /// o "principal" e são usados em snapshots de pedidos. Cadastros ricos
    /// (vários endereços, telefones, etc) ficam nas tabelas auxiliares e
    /// podem ser expandidos sem mudar a entidade raiz.
    /// </summary>
    public class Cliente
    {
        public Guid Id { get; set; }
        public Guid EmpresaId { get; set; }

        public string Nome { get; set; } = null!;

        // ── Campos primários (snapshot rápido — usado pelo app) ─────────
        public string? Apt { get; set; }
        public string? Endereco { get; set; }
        public string? Telefone { get; set; }
        public string? Email { get; set; }
        public string? Documento { get; set; }
        public string? Observacoes { get; set; }

        // ── Aditivos storefront (TASK-EZ-005) ───────────────────────────
        // Cliente storefront se cadastra com nome + telefone (OTP); demais campos
        // são preenchidos no primeiro checkout. Cpf separado de Documento porque
        // Documento é genérico no ERP; Cpf valida 11 chars no Config.
        public string? Cep { get; set; }
        public string? Complemento { get; set; }
        public string? Bairro { get; set; }
        public string? Cidade { get; set; }
        public string? Cpf { get; set; }

        /// <summary>
        /// SHA-256 hex (64 chars) do telefone normalizado em E.164.
        /// Preenchido na primeira autenticação via OTP (AUTH-002).
        /// Permite lookup seguro sem armazenar o número em claro para storefront.
        /// Null para clientes criados antes do storefront OTP.
        /// </summary>
        public string? TelefoneHash { get; set; }

        /// <summary>Último acesso autenticado via storefront (cookie de sessão).</summary>
        public DateTime? UltimoAcessoStorefrontEm { get; set; }

        /// <summary>LGPD Art. 7º — opt-in explícito para marketing (SMS/email/WhatsApp promocional).</summary>
        public bool ConsentiuMarketing { get; set; }

        /// <summary>Carimbo do opt-in (LGPD: precisa registrar quando consentimento foi dado).</summary>
        public DateTime? ConsentimentoEm { get; set; }

        // ── Métricas operacionais (mantidas pelo sync) ──────────────────
        public int OrderCount { get; set; }
        public DateTime? LastOrderAt { get; set; }

        public bool Ativo { get; set; }
        public DateTime CriadoEm { get; set; }
        public DateTime AlteradoEm { get; set; }

        public Empresa? Empresa { get; set; }

        // ── Coleções 1:N (expansíveis) ──────────────────────────────────
        public ICollection<ClienteEndereco> Enderecos { get; set; } = new List<ClienteEndereco>();
        public ICollection<ClienteTelefone> Telefones { get; set; } = new List<ClienteTelefone>();
        public ICollection<ClienteDocumento> Documentos { get; set; } = new List<ClienteDocumento>();
        public ICollection<ClienteAlteracao> Alteracoes { get; set; } = new List<ClienteAlteracao>();

        public static Cliente Criar(Guid empresaId, string nome)
        {
            var agora = DateTime.UtcNow;
            return new Cliente
            {
                Id = Guid.NewGuid(),
                EmpresaId = empresaId,
                Nome = nome.Trim(),
                Ativo = true,
                CriadoEm = agora,
                AlteradoEm = agora
            };
        }

        /// <summary>
        /// Factory para criação de cliente via storefront OTP (AUTH-002).
        /// Usa <see cref="TimeProvider"/> para testes determinísticos.
        /// </summary>
        public static Cliente CriarParaStorefront(Guid empresaId, string telefoneHash, TimeProvider time)
        {
            if (empresaId == Guid.Empty)
                throw new ArgumentException("EmpresaId é obrigatório.", nameof(empresaId));
            if (string.IsNullOrWhiteSpace(telefoneHash))
                throw new ArgumentException("TelefoneHash é obrigatório.", nameof(telefoneHash));
            ArgumentNullException.ThrowIfNull(time);

            var agora = time.GetUtcNow().UtcDateTime;
            return new Cliente
            {
                Id = Guid.NewGuid(),
                EmpresaId = empresaId,
                Nome = string.Empty,
                TelefoneHash = telefoneHash,
                UltimoAcessoStorefrontEm = agora,
                Ativo = true,
                CriadoEm = agora,
                AlteradoEm = agora,
            };
        }

        /// <summary>Registra acesso storefront — atualiza <see cref="UltimoAcessoStorefrontEm"/>.</summary>
        public void RegistrarAcessoStorefront(TimeProvider time)
        {
            ArgumentNullException.ThrowIfNull(time);
            var agora = time.GetUtcNow().UtcDateTime;
            UltimoAcessoStorefrontEm = agora;
            AlteradoEm = agora;
        }

        public void AtualizarCadastro(
            string nome,
            string? apt,
            string? endereco,
            string? telefone,
            string? email,
            string? documento,
            string? observacoes)
        {
            if (string.IsNullOrWhiteSpace(nome))
                throw new ArgumentException("Nome é obrigatório.", nameof(nome));

            Nome = nome.Trim();
            Apt = string.IsNullOrWhiteSpace(apt) ? null : apt.Trim();
            Endereco = string.IsNullOrWhiteSpace(endereco) ? null : endereco.Trim();
            Telefone = string.IsNullOrWhiteSpace(telefone) ? null : telefone.Trim();
            Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
            Documento = string.IsNullOrWhiteSpace(documento) ? null : documento.Trim();
            Observacoes = string.IsNullOrWhiteSpace(observacoes) ? null : observacoes.Trim();
            AlteradoEm = DateTime.UtcNow;
        }

        public void RegistrarPedido(DateTime quandoUtc)
        {
            OrderCount++;
            if (LastOrderAt == null || quandoUtc > LastOrderAt) LastOrderAt = quandoUtc;
            AlteradoEm = DateTime.UtcNow;
        }

        public void Desativar() { Ativo = false; AlteradoEm = DateTime.UtcNow; }
        public void Reativar() { Ativo = true; AlteradoEm = DateTime.UtcNow; }
    }

    /// <summary>
    /// Endereço extra do cliente. Cliente pode ter vários (casa, trabalho,
    /// entrega alternativa). Um deles é Padrao=true.
    /// </summary>
    public class ClienteEndereco
    {
        public Guid Id { get; set; }
        public Guid ClienteId { get; set; }

        /// <summary>"residencial", "comercial", "entrega", "outro".</summary>
        public string? Tipo { get; set; }

        public string? Logradouro { get; set; }
        public string? Numero { get; set; }
        public string? Complemento { get; set; }
        public string? Bairro { get; set; }
        public string? Cidade { get; set; }
        public string? Estado { get; set; }
        public string? Cep { get; set; }
        public string? Pais { get; set; }
        public string? Referencia { get; set; }

        public bool Padrao { get; set; }
        public DateTime CriadoEm { get; set; }
        public DateTime AlteradoEm { get; set; }

        public Cliente? Cliente { get; set; }
    }

    /// <summary>Telefone extra. Cliente pode ter vários. Um Principal.</summary>
    public class ClienteTelefone
    {
        public Guid Id { get; set; }
        public Guid ClienteId { get; set; }

        /// <summary>"celular", "fixo", "trabalho", "recado".</summary>
        public string? Tipo { get; set; }
        public string Numero { get; set; } = null!;
        public bool Whatsapp { get; set; }
        public bool Principal { get; set; }
        public string? Observacao { get; set; }

        public DateTime CriadoEm { get; set; }
        public DateTime AlteradoEm { get; set; }

        public Cliente? Cliente { get; set; }
    }

    /// <summary>Documentos: CPF, CNPJ, RG, passaporte, etc.</summary>
    public class ClienteDocumento
    {
        public Guid Id { get; set; }
        public Guid ClienteId { get; set; }

        /// <summary>"cpf", "cnpj", "rg", "passaporte", "cnh", "outro".</summary>
        public string Tipo { get; set; } = "outro";
        public string Valor { get; set; } = null!;
        public string? Emissor { get; set; }
        public DateTime? EmitidoEm { get; set; }
        public DateTime? ValidoAte { get; set; }
        public bool Principal { get; set; }

        public DateTime CriadoEm { get; set; }
        public DateTime AlteradoEm { get; set; }

        public Cliente? Cliente { get; set; }
    }

    /// <summary>
    /// Audit de alterações do cliente. Espelho do <see cref="ProdutoAlteracao"/>:
    /// armazena diff campo-a-campo com quem alterou e quando.
    /// </summary>
    public class ClienteAlteracao
    {
        public Guid Id { get; set; }
        /// <summary>
        /// F10-A: tenant isolation. Sem este campo o Global Query Filter nao
        /// aplica e a tabela fica cross-tenant. Backfill via migration:
        /// UPDATE ca SET "EmpresaId" = c."EmpresaId" FROM clientes c
        /// WHERE c."Id" = ca."ClienteId".
        /// </summary>
        public Guid EmpresaId { get; set; }
        public Guid ClienteId { get; set; }
        public Guid? AlteradoPorUserId { get; set; }
        public string? AlteradoPorNome { get; set; }
        public string Campo { get; set; } = null!;
        public string? ValorAntigo { get; set; }
        public string? ValorNovo { get; set; }
        public DateTime AlteradoEm { get; set; }
        public string? Origem { get; set; } // "web" | "mobile" | "api"

        public Cliente? Cliente { get; set; }
    }
}
