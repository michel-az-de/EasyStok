import { useCallback, useEffect, useState } from 'react'
import * as api from '../infra/api/caixaEmailApi'
import { corpoDaCaixa, formularioDaCaixa, problemaDaCaixa } from '../dominio/email'

// Caixa de suporte da loja (#1432): carrega o que está gravado (sem senha),
// testa a conexão e salva. A senha digitada some do formulário depois de
// salvar: a tela nunca guarda nem recebe a senha de volta.
//
// estado: carregando | pronto | testando | salvando | erro
export function useCaixaEmailApi() {
  const [estado, setEstado] = useState('carregando')
  const [gravada, setGravada] = useState(null)
  const [form, setForm] = useState(formularioDaCaixa(null))
  const [erro, setErro] = useState(null)
  const [teste, setTeste] = useState(null)
  const [aviso, setAviso] = useState(null)

  useEffect(() => {
    let vivo = true
    api.obterCaixaEmail()
      .then((c) => { if (vivo) { setGravada(c); setForm(formularioDaCaixa(c)); setEstado('pronto') } })
      .catch((e) => { if (vivo) { setErro(`A caixa de e-mail não carregou: ${e.message}`); setEstado('erro') } })
    return () => { vivo = false }
  }, [])

  const mudar = useCallback((campo, valor) => {
    setForm((f) => ({ ...f, [campo]: valor }))
    setTeste(null)
    setAviso(null)
  }, [])

  const validar = useCallback(() => {
    const problema = problemaDaCaixa(form, Boolean(gravada?.senhaDefinida))
    setErro(problema)
    return !problema
  }, [form, gravada])

  const testar = useCallback(() => {
    if (!validar()) return
    setEstado('testando')
    setTeste(null)
    api.testarCaixaEmail(corpoDaCaixa(form))
      .then((r) => { setTeste(r); setEstado('pronto') })
      .catch((e) => { setErro(`O teste não rodou: ${e.message}`); setEstado('pronto') })
  }, [form, validar])

  const salvar = useCallback(() => {
    if (!validar()) return
    setEstado('salvando')
    api.salvarCaixaEmail(corpoDaCaixa(form))
      .then((c) => {
        setGravada(c)
        setForm(formularioDaCaixa(c))
        setAviso('Caixa salva. Os e-mails novos entram na caixa de entrada em até um minuto.')
        setEstado('pronto')
      })
      .catch((e) => { setErro(`Não salvou: ${e.message}`); setEstado('pronto') })
  }, [form, validar])

  return { estado, gravada, form, erro, teste, aviso, mudar, testar, salvar }
}
