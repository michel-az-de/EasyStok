import { useEffect, useRef, useState } from 'react'
import { perguntarSobreOCliente, useUltimoPedidoDoCliente } from '../../../aplicacao/assistenteDoCliente'
import { useAcessoModulos } from '../../../aplicacao/acessoModulos'
import { AvatarAssistente } from '../../../componentes/AvatarAssistente'
import { Botao } from '../../../componentes/Botao'
import { Pilula } from '../../../componentes/Pilula'
import { ehRestricao, rotuloDaTag } from '../../../dominio/cliente'
import { leituraDoCliente, proximoPassoDoCliente } from '../../../dominio/leituraDoCliente'
import { perguntaSobreOCliente, pratosParaOferecer } from '../../../dominio/pratosParaOferecer'
import { primeiroNome } from '../../../dominio/mensagem'
import css from './leituraDoCliente.module.css'

// Nome que parece telefone (só dígitos e sinais) não vai para o modelo.
const nomeParaAPergunta = (nome) => { const limpo = String(nome ?? '').trim(); return /^[\d+()\s-]+$/.test(limpo) ? '' : primeiroNome(limpo) }
const MODOS = { pratos: 'Sugerir oferta', recompra: 'Escrever mensagem de recompra' }

// #3344: o painel que abre a Ficha. Leitura do cliente com as palavras da lista de Clientes (estágio, risco, última
// compra, reclamações), o que oferecer (só de compra real) e o Assistente de IA, que age só no clique e devolve texto
// para revisar. O editor remonta o painel por cliente (key), então o estado nasce limpo a cada ficha. `relacao` é a linha da lista; sem ela (ficha aberta fora da lista) o painel mostra só o que o dossiê sabe.
export function LeituraDoCliente({ cliente, relacao, relacionamento, contexto, agora, restricoes = [] }) {
  const { acoes } = useAcessoModulos()
  const leitura = leituraDoCliente(relacao, agora)
  const ultimoPedido = useUltimoPedidoDoCliente(cliente.id, contexto, acoes.verPedidos === true)
  const pratos = pratosParaOferecer({ favorito: relacionamento?.favorito, ultimoPedido })
  const passo = proximoPassoDoCliente(leitura)
  const alergias = (relacionamento?.tags ?? []).filter((t) => ehRestricao(t, restricoes)).map(rotuloDaTag)
  const podeRecomprar = Boolean(leitura?.ultimaCompra) && pratos.length > 0
  const [pedido, definir] = useState({ modo: null, carregando: false, texto: null, erro: null, copiado: false })
  const controle = useRef(null)
  useEffect(() => () => controle.current?.abort(), [])
  if (!leitura && pratos.length === 0 && !relacionamento) return null

  async function perguntar(modo) {
    if (pedido.carregando) return
    controle.current?.abort()
    const sinal = (controle.current = new AbortController()).signal
    definir({ modo, carregando: true, texto: null, erro: null, copiado: false })
    try {
      const texto = await perguntarSobreOCliente(perguntaSobreOCliente(modo, { primeiroNome: nomeParaAPergunta(cliente.nome), leitura, pratos, restricoes: alergias }), { sinal })
      if (!sinal.aborted) definir({ modo, carregando: false, texto, erro: null, copiado: false })
    } catch (e) {
      if (!sinal.aborted) definir({ modo, carregando: false, texto: null, erro: e.message || 'O Assistente não respondeu agora. Tente de novo.', copiado: false })
    }
  }
  async function copiar() {
    try { await navigator.clipboard.writeText(pedido.texto); definir((p) => ({ ...p, copiado: true, erro: null })) } catch { definir((p) => ({ ...p, erro: 'Não deu para copiar. Selecione o texto e copie.' })) }
  }

  return <section className={css.painel} aria-label="Leitura do cliente" data-leitura-do-cliente>
    {leitura && <div className={css.leitura}>
      <div className={css.estagio}>
        <strong>{leitura.estagio}</strong>
        <span className={css.passos} role="img" aria-label={`Estágio ${leitura.posicao + 1} de 4: ${leitura.estagio}`}>{[0, 1, 2, 3].map((i) => <i key={i} className={i <= leitura.posicao ? css.aceso : undefined} />)}</span>
        <small aria-hidden="true">{leitura.posicao + 1} de 4</small>
      </div>
      <div className={css.sinais}>
        {alergias.length > 0 && <Pilula tom="aviso" tamanho="inline" titulo="Restrição alimentar do cadastro.">{`Restrição: ${alergias.join(', ')}`}</Pilula>}
        {leitura.emRisco && <Pilula tom="aviso" tamanho="inline" titulo="Comprou 2 vezes ou mais e está há 30 dias sem pedir.">Em risco</Pilula>}
        {leitura.reclamacaoAberta && <Pilula tom="perigo" tamanho="inline">{leitura.reclamacaoAberta}</Pilula>}
        {leitura.ultimaCompra && <span>{leitura.ultimaCompra}</span>}
        {leitura.reclamacaoResolvida && <span>{leitura.reclamacaoResolvida}</span>}
      </div>
      {passo && <p className={leitura.reclamacaoAberta ? `${css.passo} ${css.passoUrgente}` : css.passo}>{passo}</p>}
    </div>}
    <div className={css.linha}>
    <div className={css.oferta}>
      <h3>O que oferecer</h3>
      {pratos.length > 0
        ? <ul className={css.pratos}>{pratos.map((p) => <li key={p.nome}><strong>{p.nome}</strong><small>{p.motivo}</small></li>)}</ul>
        : <p className={css.vazio}>{Number(relacao?.compras) === 0 ? 'Sem compras ainda. Registre um pedido para ver sugestões.' : 'Sem sugestão de pratos por enquanto.'}</p>}
    </div>
    {pratos.length > 0 && <div className={css.assistente}>
      <span className={css.quem}><AvatarAssistente tamanho={24} decorativo />Assistente</span>
      {Object.entries(MODOS).filter(([modo]) => modo !== 'recompra' || podeRecomprar).map(([modo, rotulo]) => <Botao key={modo} disabled={pedido.carregando} onClick={() => perguntar(modo)}>{pedido.carregando && pedido.modo === modo ? 'Pensando…' : rotulo}</Botao>)}
    </div>}
    </div>
    <output aria-live="polite" className={css.resposta}>
      {pedido.texto && <>
        <p>{pedido.texto}</p>
        <div className={css.respostaAcoes}>
          <small>Só você vê. Nada foi enviado ao cliente.</small>
          <Botao variante="texto" onClick={copiar}>{pedido.copiado ? 'Copiado' : 'Copiar'}</Botao>
          <Botao variante="texto" onClick={() => definir({ modo: null, carregando: false, texto: null, erro: null, copiado: false })}>Descartar</Botao>
        </div>
      </>}
    </output>
    {pedido.erro && <p role="alert" className={css.erro}>{pedido.erro}</p>}
  </section>
}
