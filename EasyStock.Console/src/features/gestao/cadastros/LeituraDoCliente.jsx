import { useEffect, useRef, useState } from 'react'
import { perguntarSobreOCliente, useUltimoPedidoDoCliente } from '../../../aplicacao/assistenteDoCliente'
import { useAcessoModulos } from '../../../aplicacao/acessoModulos'
import { AvatarAssistente } from '../../../componentes/AvatarAssistente'
import { Icone } from '../../../componentes/Icone'
import { Botao } from '../../../componentes/Botao'
import { Pilula } from '../../../componentes/Pilula'
import { ehRestricao, rotuloDaTag } from '../../../dominio/cliente'
import { DIAS_PARADA, ESTAGIOS_DA_LEITURA, leituraDoCliente, proximoPassoDoCliente, ritmoDoCliente } from '../../../dominio/leituraDoCliente'
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
  const ritmo = ritmoDoCliente(relacionamento?.ritmo)
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

  // #3387: a leitura vira o destaque da ficha. Primeiro o que não pode passar batido (restrição alimentar), depois o
  // número que decide a conversa (há quantos dias pediu, ao lado do ritmo que a API calculou), a régua do estágio com
  // os nomes dos passos, e o próximo passo em frase. "O que oferecer" e o Assistente seguem abaixo, como antes.
  return <section className={css.painel} aria-label="Leitura do cliente" data-leitura-do-cliente>
    {alergias.length > 0 && <p className={css.restricao} data-restricao-da-ficha>
      <Icone nome="alerta" tamanho={20} /><span><strong>Restrição alimentar</strong> {alergias.join(', ')}</span>
    </p>}
    {leitura && <div className={css.leitura}>
      <div className={leitura.parada ? `${css.recencia} ${css.parada}` : css.recencia} data-recencia-da-ficha>
        <strong>{leitura.parada && <Icone nome="alerta" tamanho={24} />}{leitura.parada && ritmo && <span className="sr">{DIAS_PARADA} dias ou mais sem pedir: </span>}{leitura.ultimaCompra ?? 'Sem data da última compra'}</strong>
        {ritmo ? <small>{ritmo.texto}<span className="sr">. </span><span>{ritmo.apoio}</span></small> : leitura.parada ? <small>{DIAS_PARADA} dias ou mais sem pedir</small> : null}
      </div>
      <div className={css.estagio}>
        <span className={css.passos} role="img" aria-label={`Estágio ${leitura.posicao + 1} de 4: ${leitura.estagio}`}>
          {ESTAGIOS_DA_LEITURA.map((nome, i) => <span key={nome} className={i < leitura.posicao ? css.feito : i === leitura.posicao ? css.atual : undefined}>
            <i /><small aria-hidden="true">{nome}</small>
          </span>)}
        </span>
      </div>
      {(leitura.emRisco || leitura.reclamacaoAberta || leitura.reclamacaoResolvida) && <div className={css.sinais}>
        {leitura.emRisco && <Pilula tom="aviso" tamanho="inline" titulo="Comprou 2 vezes ou mais e está há 30 dias sem pedir.">Em risco</Pilula>}
        {leitura.reclamacaoAberta && <Pilula tom="perigo" tamanho="inline">{leitura.reclamacaoAberta}</Pilula>}
        {leitura.reclamacaoResolvida && <span>{leitura.reclamacaoResolvida}</span>}
      </div>}
      {passo && <p className={leitura.reclamacaoAberta ? `${css.passo} ${css.passoUrgente}` : css.passo} data-passo-da-ficha>
        <Icone nome={leitura.reclamacaoAberta ? 'alerta' : 'dica'} tamanho={20} /><span>{passo}</span>
      </p>}
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
