import { useId, useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { BotaoIcone } from '../../componentes/BotaoIcone'
import { Chip } from '../../componentes/Chip'
import { Icone } from '../../componentes/Icone'
import { Pilula } from '../../componentes/Pilula'
import { moeda } from '../../dominio/formato'
import { rotuloDaTag } from '../../dominio/tag'
import css from './assistente.module.css'

// Casca comum aos seis cartões: ícone, título, "ok, entendi" (seção D da
// pesquisa: "cada cartão é dispensável"), corpo livre. Nenhum cartão bloqueia
// nada: dispensar só tira ele da vista desta conversa, nunca muda o pedido.
// #3386 (r6 H8): em tela baixa os cartões abrem recolhidos (título, com o resumo à vista); quem precisa do detalhe o abre.
// Senão dois cartões empurravam o campo de pergunta e cortavam o perfil sob ele (1024x768 e 1280x800).
const TELA_BAIXA = '(max-height: 899px)'
const abrirRecolhido = () => typeof window !== 'undefined' && window.matchMedia?.(TELA_BAIXA).matches
// #3386 (revisão): cartão de alerta (tom negativo, dica do CDC, foto sem análise) nunca abre recolhido; recolhido à mão,
// o resumo diz o estado em texto (a cor sozinha não basta, WCAG 1.4.1).
function Cartao({ icone, titulo, tom, aoDispensar, resumo, alerta = false, children }) {
  const [aberto, setAberto] = useState(() => alerta || !abrirRecolhido())
  const idCorpo = useId()
  return (
    <article className={`${css.cartao} ${tom ? css[tom] : ''}`}>
      <div className={css.cartaoTopo}>
        <h3>
          <button type="button" className={css.cartaoAlternar} aria-expanded={aberto} aria-controls={idCorpo} onClick={() => setAberto((v) => !v)}>
            <Icone nome={icone} tamanho={20} /> {titulo} <Icone nome={aberto ? 'chevron-up' : 'chevron-down'} tamanho={16} />
          </button>
        </h3>
        <BotaoIcone rotulo={`Dispensar ${titulo.toLowerCase()}`} icone="x" onClick={aoDispensar} />
      </div>
      {!aberto && resumo}
      <div className={css.cartaoCorpo} id={idCorpo} hidden={!aberto}>{children}</div>
    </article>
  )
}

// "HD aqui da cabeça" (entrevista, 2.2): quem é o cliente, num relance.
export function CartaoCliente({ dado, aoDispensar }) {
  // A restrição alimentar não pode escapar: as tags ficam à vista também com o cartão recolhido.
  const tags = dado.tags.length > 0 && (
    <div className={css.tagsCliente}>
      {dado.tags.map((tag) => <Chip key={tag} papel="tag">{rotuloDaTag(tag)}</Chip>)}
    </div>
  )
  return (
    <Cartao icone="ficha" titulo="Perfil do cliente" aoDispensar={aoDispensar} resumo={tags}>
      {tags}
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
    <Cartao icone="termometro" titulo="Tom da conversa" tom={dado.classificacao === 'negativo' ? 'perigo' : 'ok'} aoDispensar={aoDispensar}
      alerta={dado.classificacao === 'negativo'} resumo={<p><Pilula tom={TOM_SENTIMENTO[dado.classificacao]} fina>{ROTULO_SENTIMENTO[dado.classificacao]}</Pilula></p>}>
      <p><Pilula tom={TOM_SENTIMENTO[dado.classificacao]}>{ROTULO_SENTIMENTO[dado.classificacao]}</Pilula></p>
      {dado.trecho && <p>"{dado.trecho}"</p>}
      <p>Interpretação da mensagem. Confira a conversa antes de usar.</p>
    </Cartao>
  )
}

// US-049/US-050, RN-35/RN-36: dica para a Thatiane decidir, nunca uma resposta
// que sai sozinha para o cliente.
export function CartaoCDC({ dado, aoDispensar }) {
  return (
    <Cartao icone="dica" titulo={dado.titulo} tom="perigo" aoDispensar={aoDispensar} alerta resumo={<p><Pilula tom="perigo" fina>{dado.artigo}</Pilula></p>}>
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
    <Cartao icone="camera" titulo="Exemplo de análise de foto" tom="perigo" aoDispensar={aoDispensar} alerta resumo={<p>Esta foto não foi analisada.</p>}>
      <p>Demonstração do protótipo. Esta foto não foi analisada.</p>
      <p>Valor simulado: <b>{Math.round(dado.confianca * 100)}%</b></p>
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
      {aoAplicar && <div className={css.acaoCartao}>
        <Botao variante="secundario" icone="modelo" onClick={() => aoAplicar(dado.sugestaoTexto)}>
          Usar sugestão no rascunho
        </Botao>
      </div>}
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
