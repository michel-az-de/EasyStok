import { useLayoutEffect, useRef, useState } from 'react'
import { inferirCadastroDaConversa } from '../infra/api/assistenteApi'
import { identidadeDaAssistencia, useContextoAssistencia } from './contextoAssistencia'

export function useSugestaoCadastro({ conversa, conversaId, clienteId = null, vinculoRevisao = 0, revisaoFormulario = '', aoAplicar }) {
  const { identidade, chave: chaveConversa } = useContextoAssistencia({ ...conversa, id: conversaId, clienteId, vinculoRevisao })
  const chave = JSON.stringify([chaveConversa, revisaoFormulario])
  const [contexto, setContexto] = useState(chave)
  const [resposta, setResposta] = useState(null)
  const [carregando, setCarregando] = useState(false)
  const [erro, setErro] = useState(null)
  const pedido = useRef(null)
  const atual = useRef(chave)
  const aplicando = useRef(new Set())
  useLayoutEffect(() => { atual.current = chave }, [chave])
  const aindaAtual = () => atual.current === chave && identidadeDaAssistencia() === identidade
  if (contexto !== chave) {
    setContexto(chave); setResposta(null); setCarregando(false); setErro(null)
  }
  useLayoutEffect(() => {
    aplicando.current = new Set()
    return () => { pedido.current?.abort(); pedido.current = null }
  }, [chave])

  async function consultar() {
    if (!conversaId || !aindaAtual() || pedido.current) return false
    const controle = new AbortController()
    pedido.current = controle; setCarregando(true); setErro(null); setResposta(null)
    try {
      const dados = await inferirCadastroDaConversa(conversaId, { sinal: controle.signal, identidadeEsperada: identidade })
      if (controle.signal.aborted || !aindaAtual()) return false
      if (dados?.conversaId !== conversaId || (dados.clienteId ?? null) !== clienteId || dados.vinculoRevisao !== vinculoRevisao)
        throw new Error('O vínculo da conversa mudou. Confira o cadastro antes de consultar novamente.')
      if (!Array.isArray(dados.campos) || dados.campos.length > 3 || dados.campos.some(c =>
        !['nome', 'telefone', 'email'].includes(c?.campo) || typeof c.valor !== 'string' || !c.valor.trim()
        || typeof c.evidencia !== 'string' || !c.evidencia.trim() || typeof c.mensagemId !== 'string')
        || new Set(dados.campos.map(c => c.campo)).size !== dados.campos.length)
        throw new Error('O Assistente não trouxe uma sugestão válida. Tente novamente.')
      setResposta({ chave, campos: dados.campos })
      return true
    } catch (e) {
      if (!controle.signal.aborted && aindaAtual()) setErro(e.codigo === 'ASSISTENTE_INDISPONIVEL'
        ? 'O Assistente está indisponível. Você pode preencher o cadastro.' : e.message || 'Não foi possível consultar o Assistente.')
      return false
    } finally {
      if (pedido.current === controle) {
        pedido.current = null
        if (aindaAtual()) setCarregando(false)
      }
    }
  }

  const campos = resposta?.chave === chave ? resposta.campos : []
  async function aplicar(campo) {
    const proposta = campos.find(c => c.campo === (typeof campo === 'string' ? campo : campo?.campo))
    if (!proposta || !aoAplicar || !aindaAtual() || aplicando.current.has(proposta.campo)) return false
    const executando = aplicando.current
    executando.add(proposta.campo)
    try {
      if (await aoAplicar(proposta.campo, proposta.valor) === false) return false
      if (!aindaAtual()) return false
      setResposta(r => r?.chave === chave ? { ...r, campos: r.campos.filter(c => c.campo !== proposta.campo) } : r)
      return true
    } catch (e) {
      if (aindaAtual()) setErro(e.message || 'Não foi possível aplicar o campo.')
      return false
    } finally { executando.delete(proposta.campo) }
  }
  return { campos, carregando, erro, consultar, aplicar }
}
