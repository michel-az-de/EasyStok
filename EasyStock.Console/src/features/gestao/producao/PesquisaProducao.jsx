import { useEffect, useId, useRef, useState } from 'react'
import { Botao } from '../../../componentes/Botao'
import { CampoTexto } from '../../../componentes/Campo'
import { Modal } from '../../../componentes/Modal'
import { pesquisarInsumosWeb, pesquisarReceitaIa } from '../../../aplicacao/calculosReceita'
import { combinarSinais } from '../../../aplicacao/sinais'
import css from './pesquisaProducao.module.css'

function Fontes({ fontes }) {
  return <ul className={css.fontes}>{fontes.map((fonte) => <li key={fonte.url}><a href={fonte.url} target="_blank" rel="noopener noreferrer">{fonte.titulo}</a> · consultada em {new Date(fonte.consultadaEm).toLocaleDateString('pt-BR')}</li>)}</ul>
}

export function NomeInsumoPesquisado({ value, onChange, disabled }) {
  const id = useId()
  const [fontes, setFontes] = useState([])
  const [erro, setErro] = useState(null)
  useEffect(() => {
    setFontes([]); setErro(null)
    if (disabled || value.trim().length < 3 || value.trim().length > 100) return undefined
    const controle = new AbortController()
    const tempo = setTimeout(() => {
      pesquisarInsumosWeb(value.trim(), combinarSinais([controle.signal, AbortSignal.timeout(15000)]))
        .then((dados) => { if (!controle.signal.aborted) setFontes(dados) })
        .catch(() => { if (!controle.signal.aborted) setErro('Pesquisa indisponível. Você pode continuar escrevendo o nome.') })
    }, 700)
    return () => { clearTimeout(tempo); controle.abort() }
  }, [value, disabled])
  return <div className={css.nome}><CampoTexto disabled={disabled} rotulo="Nome do insumo" dica="Sugestões da Wikipédia" placeholder="Ex.: farinha de trigo" maxLength={180} value={value} onChange={onChange} list={id} autoComplete="off" />
    <datalist id={id}>{fontes.map((fonte) => <option key={fonte.url} value={fonte.titulo} />)}</datalist>
    {erro && <p role="status">{erro}</p>}
    {!!fontes.length && <Fontes fontes={fontes} />}
  </div>
}

export function PesquisaReceitaIa({ nome, aoAplicar, disabled }) {
  const [aberto, setAberto] = useState(false)
  return <><Botao icone="sparkles" aria-haspopup="dialog" disabled={disabled} onClick={() => setAberto(true)}>Pesquisar receita com IA</Botao>
    {aberto && <ModalPesquisaReceita nome={nome} aoAplicar={(sugestao) => { aoAplicar(sugestao); setAberto(false) }} aoFechar={() => setAberto(false)} />}
  </>
}

function ModalPesquisaReceita({ nome, aoAplicar, aoFechar }) {
  const [termo, setTermo] = useState(nome)
  const [resultado, setResultado] = useState(null)
  const [erro, setErro] = useState(null)
  const [ocupado, setOcupado] = useState(false)
  const controle = useRef(null)
  const formId = useId()
  useEffect(() => () => controle.current?.abort(), [])
  const pesquisar = async (evento) => {
    evento.preventDefault()
    evento.stopPropagation()
    if (controle.current) return
    const atual = new AbortController()
    controle.current = atual
    setOcupado(true); setResultado(null); setErro(null)
    try {
      const dados = await pesquisarReceitaIa(termo.trim(), combinarSinais([atual.signal, AbortSignal.timeout(90000)]))
      if (!atual.signal.aborted) setResultado(dados)
    } catch (e) { if (!atual.signal.aborted) setErro(e.message) }
    finally { if (!atual.signal.aborted) { controle.current = null; setOcupado(false) } }
  }
  return <Modal titulo="Pesquisar receita com IA" aoFechar={aoFechar} largura="min(900px, calc(100vw - 24px))" rodape={<>
    <Botao onClick={aoFechar}>Cancelar</Botao>
    <Botao tipo="submit" form={formId} disabled={ocupado || termo.trim().length < 3 || termo.trim().length > 100}>{ocupado ? 'Pesquisando…' : 'Pesquisar nas fontes públicas'}</Botao>
    {resultado && <Botao variante="primario" onClick={() => aoAplicar(resultado)}>Aplicar à receita para revisar</Botao>}
  </>}>
    <form id={formId} onSubmit={pesquisar}><CampoTexto rotulo="Nome para pesquisar" value={termo} maxLength={100} disabled={ocupado} onChange={(e) => { setTermo(e.target.value); setResultado(null) }} /></form>
    <p>Pesquisa no Livro de receitas do Wikilivros. A IA organiza a referência para você conferir. Aplicar substitui os insumos do rascunho; a receita só é gravada em Salvar receita.</p>
    {erro && <p role="alert">{erro}</p>}
    {resultado && <section aria-label="Receita sugerida">
      <p>Rendimento: {resultado.rendimento == null ? 'não informado pela fonte' : `${resultado.rendimento} ${resultado.unidadeRendimento ?? '(unidade a conferir)'}`}</p>
      <ul className={css.ingredientes}>{resultado.linhas.map((linha, indice) => <li key={indice}><strong>{linha.nome}</strong> · {linha.quantidade == null ? 'quantidade a conferir' : linha.quantidade} {linha.unidade ?? 'unidade a conferir'}</li>)}</ul>
      <p>{resultado.observacoes}</p>
      <p>Vincule cada ingrediente ao seu insumo e confira as medidas. Custo, estoque e validade usam os seus cadastros.</p>
      <Fontes fontes={resultado.fontes} />
      <p>Referências públicas do Wikilivros, sob licença <a href="https://creativecommons.org/licenses/by-sa/4.0/" target="_blank" rel="noopener noreferrer">CC BY-SA</a>.</p>
    </section>}
  </Modal>
}
