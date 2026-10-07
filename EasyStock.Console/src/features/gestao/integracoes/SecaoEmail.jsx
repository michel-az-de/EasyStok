import { Botao } from '../../../componentes/Botao'
import { CampoTexto } from '../../../componentes/Campo'
import { Pilula } from '../../../componentes/Pilula'
import { useCaixaEmailApi } from '../../../aplicacao/useCaixaEmailApi'
import { textoDoTeste } from '../../../dominio/email'
import css from './SecaoEmail.module.css'

// Caixa de suporte do atendimento por e-mail (#1432). O EasyStok lê a caixa a
// cada minuto e cada e-mail vira conversa do canal E-mail; a resposta da loja
// sai pela própria caixa. A senha nunca volta da API: o campo vazio mantém a
// gravada. Só no modo API.
export function SecaoEmail() {
  const { estado, gravada, form, erro, teste, aviso, mudar, testar, salvar } = useCaixaEmailApi()
  const ocupado = estado === 'carregando' || estado === 'testando' || estado === 'salvando'
  const campo = (nome) => ({ value: form[nome] ?? '', onChange: (e) => mudar(nome, e.target.value), disabled: ocupado })

  return (
    <section className={css.secao} aria-labelledby="secao-email-titulo">
      <h3 id="secao-email-titulo" className={css.titulo}>E-mail</h3>
      <p className={css.texto}>
        A caixa de suporte da loja. Cada e-mail que chega vira uma conversa do canal E-mail aqui, e a
        resposta sai pela própria caixa, no mesmo fio do cliente.
      </p>
      {gravada
        ? <Pilula tom="ok">Configurada: {gravada.endereco}</Pilula>
        : estado !== 'carregando' && <Pilula tom="neutro">Ainda não configurada</Pilula>}

      <div className={css.grade}>
        <CampoTexto rotulo="E-mail da caixa" tipo="email" autoComplete="off" placeholder="contato@sualoja.com" {...campo('endereco')} />
        <CampoTexto rotulo="Nome que o cliente vê" autoComplete="off" placeholder="Casa da Baba" {...campo('nomeExibicao')} />
        <CampoTexto rotulo="Servidor IMAP (leitura)" autoComplete="off" {...campo('imapHost')} />
        <CampoTexto rotulo="Porta IMAP" tipo="number" inputMode="numeric" {...campo('imapPorta')} />
        <CampoTexto rotulo="Servidor SMTP (envio)" autoComplete="off" {...campo('smtpHost')} />
        <CampoTexto rotulo="Porta SMTP" tipo="number" inputMode="numeric" {...campo('smtpPorta')} />
        <CampoTexto rotulo="Usuário" autoComplete="off" placeholder="o próprio e-mail, na Hostinger" {...campo('usuario')} />
        <CampoTexto
          rotulo="Senha"
          tipo="password"
          autoComplete="new-password"
          placeholder={gravada?.senhaDefinida ? 'Gravada. Deixe vazio para manter.' : ''}
          {...campo('senha')}
        />
      </div>
      <p className={css.explicacao}>
        Portas 993 e 465 usam SSL direto; as outras exigem STARTTLS. Nunca sai sem criptografia.
      </p>

      <div className={css.botoes}>
        <Botao variante="primario" className={css.botao} onClick={salvar} disabled={ocupado} aria-busy={estado === 'salvando'}>
          {estado === 'salvando' ? 'Salvando…' : 'Salvar'}
        </Botao>
        <Botao className={css.botao} onClick={testar} disabled={ocupado} aria-busy={estado === 'testando'}>
          {estado === 'testando' ? 'Testando…' : 'Testar conexão'}
        </Botao>
      </div>

      {erro && <p className={css.erro} role="alert">{erro}</p>}
      {aviso && <p className={css.explicacao} aria-live="polite">{aviso}</p>}
      {teste && (
        <div className={css.resultado} aria-live="polite">
          {teste.ok
            ? <Pilula tom="ok">Conexão OK: leitura e envio entraram</Pilula>
            : <Pilula tom="aviso">A conexão falhou em um dos lados</Pilula>}
          <ul className={css.linhas}>
            {textoDoTeste(teste).map((linha) => <li key={linha}>{linha}</li>)}
          </ul>
        </div>
      )}
    </section>
  )
}
