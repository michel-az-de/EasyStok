// Atendimento por e-mail (#1432). Regras puras: o assunto do fio e a caixa de
// suporte da loja (Integrações > E-mail). O dado chega por parâmetro.

// "Re:", "RES:", "Fwd:", "ENC:" e afins, repetidos, no começo do assunto. Mesma
// lista do backend (AssuntoEmail.SemPrefixos).
const PREFIXO = /^\s*(re|res|fw|fwd|enc|encaminhado|tr|aw|wg)\s*(\[\d+\])?\s*:\s*/i

export function semPrefixos(assunto) {
  let atual = (assunto ?? '').trim()
  for (;;) {
    const sem = atual.replace(PREFIXO, '').trim()
    if (sem === atual) break
    atual = sem
  }
  return atual || null
}

// O fio mostra o assunto acima do e-mail só quando ele muda: "Re: Bolo" depois
// de "Bolo" é o mesmo assunto e não se repete a cada resposta.
export function marcarMudancasDeAssunto(mensagens) {
  let anterior = null
  return (mensagens ?? []).map((m) => {
    const limpo = semPrefixos(m.assunto)
    if (!limpo || limpo === anterior) return null
    anterior = limpo
    return m.assunto
  })
}

// Padrão da Hostinger, a caixa escolhida pelo dono (contato@casadababa.com):
// IMAP 993 e SMTP 465, os dois com SSL implícito.
export const CAIXA_PADRAO = Object.freeze({
  endereco: '',
  nomeExibicao: '',
  imapHost: 'imap.hostinger.com',
  imapPorta: 993,
  smtpHost: 'smtp.hostinger.com',
  smtpPorta: 465,
  usuario: '',
  senha: '',
})

const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]+$/
const HOST = /^[a-z0-9.-]+$/i

// Formulário da tela a partir do que a API devolve (nunca vem senha).
export function formularioDaCaixa(caixa) {
  if (!caixa) return { ...CAIXA_PADRAO }
  return {
    endereco: caixa.endereco ?? '',
    nomeExibicao: caixa.nomeExibicao ?? '',
    imapHost: caixa.imapHost ?? CAIXA_PADRAO.imapHost,
    imapPorta: caixa.imapPorta ?? CAIXA_PADRAO.imapPorta,
    smtpHost: caixa.smtpHost ?? CAIXA_PADRAO.smtpHost,
    smtpPorta: caixa.smtpPorta ?? CAIXA_PADRAO.smtpPorta,
    usuario: caixa.usuario ?? '',
    senha: '',
  }
}

// Primeiro problema do formulário, em uma frase, ou null. `senhaGravada`:
// a API já tem senha, então o campo vazio mantém a gravada.
export function problemaDaCaixa(form, senhaGravada) {
  if (!EMAIL.test((form.endereco ?? '').trim())) return 'Informe o e-mail completo da caixa, como contato@sualoja.com.'
  if (!HOST.test((form.imapHost ?? '').trim())) return 'Servidor IMAP: só o nome, como imap.hostinger.com.'
  if (!HOST.test((form.smtpHost ?? '').trim())) return 'Servidor SMTP: só o nome, como smtp.hostinger.com.'
  for (const [porta, nome] of [[form.imapPorta, 'IMAP'], [form.smtpPorta, 'SMTP']]) {
    const n = Number(porta)
    if (!Number.isInteger(n) || n < 1 || n > 65535) return `Porta ${nome} inválida.`
  }
  if (!(form.usuario ?? '').trim()) return 'Informe o usuário da caixa (na Hostinger, o próprio e-mail).'
  if (!form.senha && !senhaGravada) return 'Informe a senha da caixa.'
  return null
}

// Corpo do PUT/POST: senha vazia não vai (a API mantém a gravada).
export function corpoDaCaixa(form) {
  return {
    endereco: form.endereco.trim(),
    nomeExibicao: (form.nomeExibicao ?? '').trim() || null,
    imapHost: form.imapHost.trim(),
    imapPorta: Number(form.imapPorta),
    smtpHost: form.smtpHost.trim(),
    smtpPorta: Number(form.smtpPorta),
    usuario: form.usuario.trim(),
    ...(form.senha ? { senha: form.senha } : {}),
  }
}

// Resultado do "testar conexão" em uma linha por lado.
export function textoDoTeste(r) {
  if (!r) return []
  return [
    r.imapOk ? 'Leitura (IMAP): conectou e entrou.' : `Leitura (IMAP): ${r.imapErro ?? 'falhou'}`,
    r.smtpOk ? 'Envio (SMTP): conectou e entrou.' : `Envio (SMTP): ${r.smtpErro ?? 'falhou'}`,
  ]
}
