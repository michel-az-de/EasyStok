import { Botao } from '../../componentes/Botao'
import { Chip } from '../../componentes/Chip'
import { Icone } from '../../componentes/Icone'
import { Pilula } from '../../componentes/Pilula'
import { moeda } from '../../dominio/formato'
import css from './assistente.module.css'

// Casca comum aos seis cartões: ícone, título, "ok, entendi" (seção D da
// pesquisa: "cada cartão é dispensável"), corpo livre. Nenhum cartão bloqueia
// nada: dispensar só tira ele da vista desta conversa, nunca muda o pedido.
function Cartao({ icone, titulo, tom, aoDispensar, children }) {
  return (
    <article className={`${css.cartao} ${tom ? css[tom] : ''}`}>
      <div className={css.cartaoTopo}>
        <h3><Icone nome={icone} tamanho={20} /> {titulo}</h3>
        <Botao variante="texto" className={css.dispensar} aria-label="Ok, entendi, dispensar cartão" title="Ok, entendi" onClick={aoDispensar}>
          <Icone nome="check" tamanho={18} />
        </Botao>
      </div>
      <div className={css.cartaoCorpo}>{children}</div>
    </article>
  )
}

// "HD aqui da cabeça" (entrevista, 2.2): quem é o cliente, num relance.
export function CartaoCliente({ dado, aoDispensar }) {
  return (
    <Cartao icone="ficha" titulo="Perfil do cliente" aoDispensar={aoDispensar}>
      {dado.tags.length > 0 && (
        <div className={css.tagsCliente}>
          {dado.tags.map((tag) => <Chip key={tag} papel="tag">{tag}</Chip>)}
        </div>
      )}
      <p>{dado.chegou ?? 'Sem data de chegada registrada'}</p>
      <div className={css.numeros}>
        <span><b>{dado.pedidos}</b> {dado.pedidos === 1 ? 'pedido' : 'pedidos'}</span>
        {dado.total > 0 && <span>total <b>{moeda(dado.total)}</b></span>}
        {dado.total > 0 && <span>ticket médio <b>{moeda(dado.ticketMedio)}</b></span>}
      </div>
    </Cartao>
  )
}

// US-051/RN-38: o classificador sugere, nunca prioriza nem responde sozinho.
const TOM_SENTIMENTO = { positivo: 'ok', negativo: 'perigo', neutro: 'neutro' }
const ROTULO_SENTIMENTO = { positivo: 'Positivo', negativo: 'Negativo', neutro: 'Neutro' }

export function CartaoSentimento({ dado, aoDispensar }) {
  return (
    <Cartao icone="termometro" titulo="Sentimento da conversa" tom={dado.classificacao === 'negativo' ? 'perigo' : 'ok'} aoDispensar={aoDispensar}>
      <p><Pilula tom={TOM_SENTIMENTO[dado.classificacao]}>{ROTULO_SENTIMENTO[dado.classificacao]}</Pilula></p>
      {dado.trecho && <p>"{dado.trecho}"</p>}
    </Cartao>
  )
}

// US-049/US-050, RN-35/RN-36: dica para a Thatiane decidir, nunca uma resposta
// que sai sozinha para o cliente.
export function CartaoCDC({ dado, aoDispensar }) {
  return (
    <Cartao icone="dica" titulo={dado.titulo} tom="perigo" aoDispensar={aoDispensar}>
      <p><Pilula tom="perigo" fina>{dado.artigo}</Pilula></p>
      <p>{dado.dica}</p>
    </Cartao>
  )
}

// Indício simulado, não checagem real de imagem: o cartão avisa isso duas
// vezes (rótulo do cartão e frase final) de propósito, para nenhuma Thatiane
// apressada ler "confiança 80%" como prova.
export function CartaoFotoSuspeita({ dado, aoDispensar }) {
  return (
    <Cartao icone="camera" titulo="Foto suspeita de imagem gerada por IA" tom="perigo" aoDispensar={aoDispensar}>
      <p>Confiança do indício: <b>{Math.round(dado.confianca * 100)}%</b></p>
      <ul>
        {dado.motivos.map((motivo) => <li key={motivo}>{motivo}</li>)}
      </ul>
      <p>É uma pista para você confirmar por outros meios, não uma prova.</p>
    </Cartao>
  )
}

// Exemplo do dono, 24/09 04h12: "essa pessoa sempre compra de domingo, e ela
// compra muita lasanha, que tal enviar uma promoção?". Um clique só troca o
// rascunho pelo texto pronto, a Thatiane ainda revisa e envia na mão.
export function CartaoPadraoDeCompra({ dado, aoAplicar, aoDispensar }) {
  return (
    <Cartao icone="dollar-sign" titulo="Padrão de compra" aoDispensar={aoDispensar}>
      <p>
        {dado.pedidos} pedidos até agora
        {dado.dia && <> · costuma pedir {dado.dia}</>}
        {dado.prato && <> · favorito: {dado.prato}</>}
      </p>
      <div className={css.acaoCartao}>
        <Botao variante="secundario" icone="modelo" onClick={() => aoAplicar(dado.sugestaoTexto)}>
          Usar sugestão no rascunho
        </Botao>
      </div>
    </Cartao>
  )
}

// Revê o que a Thatiane está digitando agora, não o que já foi enviado.
export function CartaoOrtografia({ dado, aoAplicar, aoDispensar }) {
  return (
    <Cartao icone="lupa" titulo="Revisão ortográfica" aoDispensar={aoDispensar}>
      <p>"{dado.corrigido}"</p>
      <div className={css.acaoCartao}>
        <Botao variante="secundario" icone="check" onClick={() => aoAplicar(dado.corrigido)}>
          Corrigir no rascunho
        </Botao>
      </div>
    </Cartao>
  )
}
