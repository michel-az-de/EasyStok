using EasyStock.Domain.ValueObjects;

namespace EasyStock.Domain.Entities
{
    public class Usuario
    {
        public Guid Id { get; set; }
        public string Nome { get; set; } = null!;
        public string Email { get; set; } = null!;
        public string? AvatarUrl { get; set; }
        public string TemaPreferido { get; set; } = "light";
        public string SenhaHash { get; set; } = null!;
        public bool Ativo { get; set; }
        public bool EmailConfirmado { get; set; }
        public DateTime? UltimoAcessoEm { get; set; }
        public DateTime CriadoEm { get; set; }
        public DateTime AlteradoEm { get; set; }
        public int FailedLoginAttempts { get; set; }
        public DateTime? LockoutEnd { get; set; }

        /// <summary>
        /// Corte das sessões (#1352): todo JWT emitido antes deste instante deixa de valer. Nulo quer dizer que
        /// nunca revogou. Só <see cref="RevogarSessoes"/> muda; o login nunca mexe (logar o tablet derrubaria o balcão).
        /// </summary>
        public DateTime? SessoesValidasDesde { get; set; }

        /// <summary>Telefone do próprio usuário em E.164 BR (N4). Nulo até ele informar. Só vira canal de aviso depois de verificado.</summary>
        public TelefoneE164? Telefone { get; set; }

        /// <summary>Quando o telefone foi verificado (N4, verificação administrativa). Nulo: não recebe WhatsApp.</summary>
        public DateTime? TelefoneVerificadoEm { get; set; }

        /// <summary>
        /// Endereço novo que espera confirmação (N4). <see cref="Email"/> e <see cref="EmailConfirmado"/> só mudam
        /// quando o link enviado a este endereço é aberto: um erro de digitação não tranca o usuário para fora.
        /// </summary>
        public string? EmailPendente { get; set; }

        /// <summary>
        /// Nivel preferencial do atendente no helpdesk (N1..N4). NULL para usuarios
        /// que nao atuam no atendimento. Define a fila de tickets que ele ve por default.
        /// </summary>
        public NivelAtendimento? NivelAtendimentoPreferido { get; set; }

        /// <summary>Quando a pessoa aceitou o convite (N9). Nulo: nunca foi convidada ou ainda não aceitou.</summary>
        public DateTime? ConviteAceitoEm { get; set; }

        /// <summary>
        /// Por onde o convite foi aceito (N9), já mascarado: <c>+55•••1234</c> (WhatsApp), <c>e-mail</c> ou <c>Google</c>.
        /// O telefone inteiro nunca vai para esta coluna (<see cref="ViaDoConvite"/>).
        /// </summary>
        public string? ConviteAceitoVia { get; set; }

        public ICollection<UsuarioEmpresa> Empresas { get; set; } = new List<UsuarioEmpresa>();
        public ICollection<UsuarioPerfil> Perfis { get; set; } = new List<UsuarioPerfil>();

        /// <summary>Tem perfil global de SuperAdmin (<c>Perfil.EmpresaId</c> nulo). Exige <see cref="Perfis"/> carregado com o perfil.</summary>
        public bool EhSuperAdmin() => Perfis.Any(up => up.Perfil is { Nivel: NivelAcesso.SuperAdmin });

        public static Usuario Criar(string nome, string email, string senhaHash)
        {
            var agora = DateTime.UtcNow;
            return new Usuario
            {
                Id = Guid.NewGuid(),
                Nome = nome,
                Email = email,
                SenhaHash = senhaHash,
                Ativo = true,
                EmailConfirmado = false,
                CriadoEm = agora,
                AlteradoEm = agora,
                FailedLoginAttempts = 0,
                LockoutEnd = null
            };
        }

        /// <summary>Prefixo do hash inutilizável do convidado (N9). Nenhum bcrypt válido começa assim: o login por senha falha.</summary>
        public const string MarcadorDeConvite = "$2a$10$CONVIDADO_";

        private const string MarcadorSemSenha = "$2a$10$SEMSENHA_";

        /// <summary>
        /// Convidado (N9): nasce <c>Ativo</c>, sem e-mail confirmado e com <see cref="MarcadorDeConvite"/> no lugar da senha.
        /// Quem nunca define senha nunca entra por senha; o aceite do convite troca o marcador pelo hash da senha escolhida.
        /// </summary>
        public static Usuario CriarConvidado(string nome, string email)
        {
            var id = Guid.NewGuid();
            return Criar(nome, email, MarcadorDeConvite + id.ToString("N"));
        }

        /// <summary>Convite ainda não aceito: a senha é o marcador. Não depende de <see cref="Ativo"/>.</summary>
        public bool ConvitePendente => SenhaHash is not null && SenhaHash.StartsWith(MarcadorDeConvite, StringComparison.Ordinal);

        /// <summary>
        /// Aceita o convite definindo a senha (N9): troca o marcador pelo hash, grava o instante e a via (já mascarada) e zera
        /// as falhas de senha. A verificação do canal (e-mail ou telefone) é do use case, que sabe qual canal do token valeu.
        /// </summary>
        public void AceitarConvite(string senhaHash, string via, DateTime agora)
        {
            if (!ConvitePendente)
                throw new InvalidOperationException("O usuário não tem convite pendente.");
            if (string.IsNullOrWhiteSpace(senhaHash))
                throw new ArgumentException("O hash da senha não pode ser vazio.", nameof(senhaHash));

            SenhaHash = senhaHash;
            RegistrarAceite(via, agora);
        }

        /// <summary>
        /// Aceita o convite sem senha (N9, login Google): o e-mail foi provado pelo Google, mas a senha segue inutilizável
        /// (outro marcador, que não é o do convite) até a pessoa definir uma por "esqueci a senha".
        /// </summary>
        public void AceitarConviteSemSenha(string via, DateTime agora)
        {
            if (!ConvitePendente)
                throw new InvalidOperationException("O usuário não tem convite pendente.");

            SenhaHash = MarcadorSemSenha + Id.ToString("N");
            EmailConfirmado = true;
            RegistrarAceite(via, agora);
        }

        private void RegistrarAceite(string via, DateTime agora)
        {
            ConviteAceitoEm = agora;
            ConviteAceitoVia = via;
            AlteradoEm = agora;
            ResetarTentativasFalha();
        }

        public void AtualizarUltimoAcesso()
        {
            UltimoAcessoEm = DateTime.UtcNow;
            AlteradoEm = DateTime.UtcNow;
        }

        public void IncrementarTentativasFalha()
        {
            FailedLoginAttempts++;
            AlteradoEm = DateTime.UtcNow;
        }

        public void ResetarTentativasFalha()
        {
            FailedLoginAttempts = 0;
            LockoutEnd = null;
            AlteradoEm = DateTime.UtcNow;
        }

        public void BloquearPorTentativas(int minutosLockout = 15)
        {
            LockoutEnd = DateTime.UtcNow.AddMinutes(minutosLockout);
            AlteradoEm = DateTime.UtcNow;
        }

        public bool EstaBloqueado()
        {
            return LockoutEnd.HasValue && LockoutEnd > DateTime.UtcNow;
        }

        /// <summary>Falhas de senha seguidas que bloqueiam a conta. O passo 1 do login e o login completo somam.</summary>
        public const int FalhasParaBloquear = 5;

        /// <summary>Minutos de bloqueio da conta que chegou ao limite de falhas.</summary>
        public const int MinutosDeBloqueio = 15;

        /// <summary>
        /// Conta uma senha errada (#1352) e bloqueia a conta por <see cref="MinutosDeBloqueio"/> min na
        /// <see cref="FalhasParaBloquear"/>ª falha seguida. Regra única do passo 1 do login (lista-empresas) e
        /// do login completo. Bloqueio já vencido não deixa falha herdada: a contagem recomeça em 1.
        /// </summary>
        public void RegistrarFalhaDeSenha()
        {
            if (LockoutEnd.HasValue && LockoutEnd.Value <= DateTime.UtcNow)
                ResetarTentativasFalha();

            IncrementarTentativasFalha();
            if (FailedLoginAttempts >= FalhasParaBloquear)
                BloquearPorTentativas(MinutosDeBloqueio);
        }

        /// <summary>
        /// Derruba as sessões emitidas até <paramref name="agora"/> (#1352): grava o corte
        /// <see cref="SessoesValidasDesde"/> truncado ao segundo (o <c>iat</c> do JWT é em segundos inteiros) e
        /// sem nunca recuar. Quem persiste é o <c>RevogadorSessoes</c>, num UPDATE atômico: o
        /// <c>UpdateAsync</c> do repositório não grava este campo.
        /// </summary>
        public void RevogarSessoes(DateTime agora)
        {
            var utc = agora.Kind == DateTimeKind.Local ? agora.ToUniversalTime() : agora;
            var corte = new DateTime(utc.Ticks - utc.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);

            if (SessoesValidasDesde is { } atual && corte <= atual)
                return;

            SessoesValidasDesde = corte;
            AlteradoEm = utc;
        }

        /// <summary>Define o telefone (N4). Telefone diferente zera a verificação: o aparelho novo não herda a confiança.</summary>
        public void DefinirTelefone(TelefoneE164 telefone)
        {
            ArgumentNullException.ThrowIfNull(telefone);
            if (Telefone != telefone)
                TelefoneVerificadoEm = null;
            Telefone = telefone;
            AlteradoEm = DateTime.UtcNow;
        }

        /// <summary>Marca o telefone como verificado em <paramref name="agora"/> (N4). Sem telefone, não há o que verificar.</summary>
        public void MarcarTelefoneVerificado(DateTime agora)
        {
            if (Telefone is null)
                throw new InvalidOperationException("Usuário sem telefone para verificar.");
            TelefoneVerificadoEm = agora;
            AlteradoEm = agora;
        }

        /// <summary>Registra o endereço novo como pendente (N4). Não mexe em <see cref="Email"/> nem em <see cref="EmailConfirmado"/>.</summary>
        public void SolicitarTrocaDeEmail(string novoEmail)
        {
            if (string.IsNullOrWhiteSpace(novoEmail))
                throw new ArgumentException("E-mail novo não pode ser vazio.", nameof(novoEmail));
            EmailPendente = novoEmail.Trim();
            AlteradoEm = DateTime.UtcNow;
        }

        /// <summary>O link do endereço novo foi aberto (N4): o pendente vira o e-mail da conta, já confirmado, e a pendência some.</summary>
        public void ConfirmarNovoEmail()
        {
            if (string.IsNullOrWhiteSpace(EmailPendente))
                throw new InvalidOperationException("Não há troca de e-mail pendente.");
            Email = EmailPendente;
            EmailPendente = null;
            EmailConfirmado = true;
            AlteradoEm = DateTime.UtcNow;
        }

        /// <summary>
        /// LGPD Art. 18 — direito ao esquecimento. Substitui campos PII por valores
        /// pseudonimizados deterministicos baseados no Id (preserva FKs em audit logs,
        /// movimentacoes e demais entidades com valor historico/forense).
        /// Login fica impossivel (SenhaHash com prefixo bcrypt invalido + Ativo=false).
        /// Operacao irreversivel.
        /// </summary>
        public void Anonimizar()
        {
            var pseudoId = Id.ToString("N").Substring(0, 12);
            Nome = "[Anonimizado]";
            Email = $"anonimizado-{pseudoId}@anonimizado.local";
            AvatarUrl = null;
            // Hash invalido com prefixo bcrypt $2a$ — impede match com qualquer senha
            // (BCrypt.Verify devolve false para hash que nao bate o pattern). Usar
            // string.Empty era inseguro: alguns caminhos comparavam literal e podiam
            // aceitar entrada em branco.
            SenhaHash = $"$2a$10$INVALIDATED_{pseudoId}";
            Ativo = false;
            EmailConfirmado = false;
            Telefone = null;
            TelefoneVerificadoEm = null;
            EmailPendente = null;
            ConviteAceitoVia = null;
            FailedLoginAttempts = 0;
            LockoutEnd = null;
            AlteradoEm = DateTime.UtcNow;
        }
    }
}
