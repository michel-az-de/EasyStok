import { useEffect, useRef, useState } from 'react'
import { useDraggable } from '@dnd-kit/core'
import { AlcaLargura } from '../../componentes/AlcaLargura'
import { Icone } from '../../componentes/Icone'
import { Pilula } from '../../componentes/Pilula'
import { Vazio } from '../../componentes/Vazio'
import { Botao } from '../../componentes/Botao'
import { useAcoes, useAtendimento, useCatalogo } from '../../aplicacao/contextos'
import { useAcaoDisponivel } from '../../aplicacao/useAcaoDisponivel'
import { useEscape } from '../../hooks/useEscape'
import { abrirSecao } from '../../hooks/useRecolhido'
import {
  adicionaisDoItem, alternativasPara, contarDisponiveis, ehNovidade,
  estaEmValidacao, itensAtivos, itensRemovidos, porcaoDoToque, produtoDaPorcao, situacaoDoItem,
} from '../../dominio/cardapio'
import { cartaDoItem } from '../../dominio/arteCardapio'
import { agruparApresentacoes } from '../../dominio/apresentacoesDoCardapio'
import { aceitaFormato, canalDaConversa, motivoDeFormato } from '../../dominio/canal'
import { permissaoDeEscrita } from '../../dominio/janela'
import {
  SEM_CATEGORIA, agruparPorCategoria, filtrarCardapio, fotoDoItem, mensagemDaFotoDoItem,
} from '../../dominio/vitrineCardapio'
import { moeda } from '../../dominio/formato'
import {
  agruparPorLinha, comandaTravada, itensDetalhados, numeroDaComanda,
} from '../../dominio/pedido'
import { totalDaVenda } from '../../dominio/resumoAtendimento'
import { diferencaAposPagamento } from '../../dominio/pagamento'
import { FOTO_NAO_ENVIADA } from '../../dominio/anexos'
import { primeiroNome } from '../../dominio/mensagem'
import { LINHA_COMANDA_TRAVADA } from '../../dominio/resumoPedido'
import { EditorItemCardapio } from './EditorItemCardapio'
import { useAcessoModulos } from '../../aplicacao/acessoModulos'
import { lerFichaDoPrato } from '../../aplicacao/edicaoCardapio'
import { MarcaIcone } from '../../componentes/MarcaIcone'
import css from './cardapio.module.css'

// Dois papéis na mesma janela, porque são a mesma conversa com o cardápio: ela
// abre para anotar o pedido e descobre ali que acabou, ou abre para acertar o
// dia e já anota. Obrigar a fechar uma tela para abrir outra é o rodeio que ela
// odeia (áudio 03).
// Só o estado que muda a venda vira texto colorido. Saldo folgado não precisa
// de marca: o número já está no contador de Gerir o dia.
const PEDE_ACAO = new Set(['esgotado', 'fora-do-dia', 'pouco'])

// #1474: largura em que nome, porção, preço e o botão de foto cabem sem espremer o nome.
const LARGURA_MINIMA = 440

const PAPEIS = [
  { id: 'escolher', rotulo: 'Anotar na comanda' },
  { id: 'gerir', rotulo: 'Gerir o dia' },
]

// Foto real do item (#1448), a mesma da vitrine. Sem foto, ou se ela não carregar, a carta
// desenhada: honesta, parece arte de cardápio e não finge ser fotografia.
function FotoDoItem({ item, className, alt = '' }) {
  const foto = fotoDoItem(item)
  const [falhou, setFalhou] = useState(null)
  const usarFoto = foto && falhou !== foto
  return (
    <img
      className={className}
      src={usarFoto ? foto : cartaDoItem(item)}
      alt={alt}
      onError={usarFoto ? () => setFalhou(foto) : undefined}
    />
  )
}

function FichaDoItem({ item, adicionais, linhas, aoEditar }) {
  const { fonteApi } = useAtendimento()
  const [detalhe, setDetalhe] = useState(null)
  const [erro, setErro] = useState(null)
  const [tentativa, setTentativa] = useState(0)
  useEffect(() => {
    if (!fonteApi) return undefined
    let vivo = true
    lerFichaDoPrato(item.sku).then((d) => { if (vivo) { setDetalhe(d); setErro(null) } })
      .catch((e) => { if (vivo) setErro(e.message) })
    return () => { vivo = false }
  }, [fonteApi, item, tentativa])
  return (
    <div className={css.ficha}>
      {(detalhe?.descricao || item.descricao) && <p className={css.descricaoPrato}>{detalhe?.descricao || item.descricao}</p>}
      {fonteApi && !detalhe && !erro && <output>Consultando informações do prato…</output>}
      {erro && <p role="alert">{erro} <Botao variante="texto" onClick={() => setTentativa((v) => v + 1)}>Tentar novamente</Botao></p>}
      <dl className={css.dados}>
        <div><dt>Como sai</dt><dd>{linhas[item.linha]?.dica}</dd></div>
        {detalhe && <>
          <div><dt>Ingredientes</dt><dd>{detalhe.ingredientes || 'Ainda não informados'}</dd></div>
          <div><dt>Alérgenos</dt><dd>{detalhe.alergenos || 'Não informados. Confirme com a cozinha.'}</dd></div>
          <div><dt>Preparo</dt><dd>{detalhe.tempoPreparoMinutos ? `${detalhe.tempoPreparoMinutos} minutos` : 'Tempo padrão da loja'}</dd></div>
          {item.linha === 'casa' && <div><dt>Em casa</dt><dd>{detalhe.instrucaoFinalizacao || 'Instruções ainda não cadastradas'}</dd></div>}
        </>}
        <div>
          <dt>Adicionais</dt>
          <dd>
            {adicionais.length === 0
              ? 'Nenhum para este item'
              : adicionais.map((a) => a.nome + ' ' + moeda(a.preco)).join(' · ')}
          </dd>
        </div>
      </dl>
      {aoEditar && <Botao variante="texto" icone="lapis" onClick={() => aoEditar(item)}>Editar prato e fotos</Botao>}
    </div>
  )
}

// O cartãozinho de 64 px que segue o dedo durante o arrasto (seção 9): só
// foto e nome, leve o bastante para acompanhar o ponteiro sem travar.
export function CartaoArrasto({ item }) {
  return (
    <div className={css.cartaoArrasto}>
      <FotoDoItem item={item} />
      <span>{item.nome}</span>
    </div>
  )
}

// Um item por instância do hook: useDraggable precisa de um componente por
// linha, senão a lista muda de tamanho e a ordem dos hooks quebra.
function ItemEscolher({
  item, situacao, quantos, alternativas, linhas, adicionais, cardapio, novidade, aoEscolher, aoAjustar, envioDeFoto,
  podeGerir = true, naComanda = () => 0, aoEditar, travada = false, compacto = false,
}) {
  const [detalheAberto, setDetalheAberto] = useState(false)
  // M1.4b (#1531): prato com porções. O toque (e o arrasto) soma a porção padrão; cada porção tem
  // o seu botão, com o preço dela, e a esgotada não entra.
  const porcoes = item.porcoes ?? []
  const padrao = porcaoDoToque(item)
  const doToque = porcoes.length > 0 ? (padrao ? produtoDaPorcao(item, padrao) : null) : item
  const { attributes, listeners, setNodeRef, isDragging } = useDraggable({
    id: 'prato-' + item.sku,
    data: { type: 'prato', item: doToque ?? item },
    disabled: !situacao.vendavel || !doToque || travada,
  })

  return (
    <li className={`${css.itemVenda} ${compacto ? css.apresentacao : ''} ${compacto && detalheAberto ? css.apresentacaoAberta : ''} ${situacao.vendavel ? '' : css.apagado}`}>
      {/* `.linhaComAjuste` só existe pra por o "−" ao LADO do <button> da
          linha, nunca dentro dele: HTML não aceita botão dentro de botão (o
          navegador reparenta e quebra o clique). Os dois são irmãos que
          dividem a largura da linha (seção 3.1). */}
      <div className={css.linhaComAjuste}>
        <button
          type="button"
          ref={setNodeRef}
          className={css.linha}
          aria-label={compacto ? `Somar ${item.nome} na comanda` : undefined}
          disabled={!situacao.vendavel || !doToque || travada}
          title={travada ? LINHA_COMANDA_TRAVADA : undefined}
          style={isDragging ? { opacity: 0.5 } : undefined}
          onClick={() => aoEscolher(doToque)}
          {...listeners}
          {...attributes}
        >
          {!compacto && <FotoDoItem item={item} className={css.foto} />}
          {/* Sete itens viravam sete manchas de cor. O estado do item é
              texto na segunda linha, e só aparece quando muda a venda. */}
          <span className={css.nome}>
            {compacto ? item.porcao : item.nome}
            {novidade && <Pilula tom="marca" fina>Novidade da casa</Pilula>}
            <small>
              {[!compacto && linhas[item.linha]?.rotulo, !compacto && item.porcao].filter(Boolean).join(' · ')}
              {PEDE_ACAO.has(situacao.chave) && (
                <b className={css[situacao.tom]}>{compacto ? '' : ' · '}{situacao.rotulo}</b>
              )}
            </small>
          </span>
          <span className={css.preco}>{moeda(item.preco)}</span>
          {!travada && (
            <span className={css.somar}>
              <Icone nome="mais" rotulo={'Somar ' + item.nome + ' na comanda, ou arrastar até a comanda'} />
            </span>
          )}
        </button>

        {/* Pílula "N na comanda" + "−" (seção 3.1): desfazer um toque errado
            sem sair do cardápio. Fora do botão da linha (largura própria,
            nunca por cima dele), então nunca disputa espaço com nome/preço
            nem depende de posição absoluta pra não se sobrepor. */}
        {quantos > 0 && porcoes.length === 0 && (
          <div className={css.ajusteNaLinha}>
            <span className={css.contagem}>{quantos} na comanda</span>
            {!travada && (
              <button
                type="button"
                className={css.botaoMenosCardapio}
                onClick={() => aoAjustar(item.sku, -1)}
              >
                <Icone nome="menos" rotulo={'Tirar uma unidade de ' + item.nome} />
              </button>
            )}
          </div>
        )}

      </div>

      {porcoes.length > 0 && (
        <ul className={css.porcoesVenda} aria-label={'Porções de ' + item.nome}>
          {porcoes.map((p) => {
            const produto = produtoDaPorcao(item, p)
            const qtd = naComanda(produto.sku)
            return (
              <li key={p.id} className={css.linhaPorcao}>
                <button
                  type="button"
                  className={css.botaoPorcao}
                  disabled={!situacao.vendavel || !p.disponivel || travada}
                  title={travada ? LINHA_COMANDA_TRAVADA : undefined}
                  onClick={() => aoEscolher(produto)}
                >
                  <span className={css.nome}>
                    {p.rotulo}
                    <small>{p.disponivel ? (p.padrao ? 'padrão' : '') : 'esgotada'}</small>
                  </span>
                  <span className={css.preco}>{moeda(p.preco)}</span>
                  {qtd === 0 && p.disponivel && !travada && <Icone nome="mais" rotulo={`Somar ${item.nome} ${p.rotulo} na comanda`} />}
                </button>
                {qtd > 0 && (
                  <div className={css.ajusteNaLinha}>
                    <span className={css.contagem}>{qtd} na comanda</span>
                    {!travada && (
                      <button type="button" className={css.botaoMenosCardapio} onClick={() => aoAjustar(produto.sku, -1)}>
                        <Icone nome="menos" rotulo={`Tirar uma unidade de ${item.nome} ${p.rotulo}`} />
                      </button>
                    )}
                  </div>
                )}
              </li>
            )
          })}
        </ul>
      )}

      {/* O erro caro não é o item acabar, é a conversa morrer em "não tem".
          A alternativa nasce junto com a má notícia. */}
      {alternativas.length > 0 && (
        <p className={css.alternativa}>
          {situacao.chave === 'fora-do-dia'
            ? (podeGerir ? 'Você tirou do dia. Ligue de volta em Gerir o dia, ou ofereça: ' : 'Fora do dia. Ofereça: ')
            : 'Sem saldo, mas ainda vende. No lugar dele: '}
          {alternativas.map((a) => a.nome).join(' ou ')}.
        </p>
      )}

      <div className={css.acoesConsulta}>
        <button type="button" className={css.consultar} aria-label={compacto ? `Ingredientes e detalhes de ${item.nome}` : undefined} aria-expanded={detalheAberto} onClick={() => setDetalheAberto((aberto) => !aberto)}>
          <MarcaIcone nome="informacao" tamanho={20} /> <span className={compacto ? 'sr' : undefined}>Ingredientes e detalhes</span>
        </button>
        {envioDeFoto && fotoDoItem(item) && (
          <button type="button" className={css.enviarFoto}
            disabled={!envioDeFoto.pode || envioDeFoto.enviando !== null}
            title={envioDeFoto.motivo ?? 'Enviar a foto ao cliente'}
            aria-label={rotuloDoEnvio(envioDeFoto, item)} onClick={() => envioDeFoto.enviar(item)}>
            <Icone nome={envioDeFoto.enviado === item.sku ? 'check' : 'imagem'} />
            <span className={compacto ? 'sr' : undefined}>{envioDeFoto.enviando === item.sku ? 'Enviando…' : envioDeFoto.enviado === item.sku ? 'Foto enviada' : 'Enviar foto'}</span>
          </button>
        )}
        {/* #1587: a falha fica escrita junto do botão; antes ele só voltava a "Enviar foto". */}
        {envioDeFoto?.falhou === item.sku && <p className={css.falhaFoto} role="alert">{FOTO_NAO_ENVIADA}</p>}
      </div>
      {detalheAberto && <FichaDoItem
          item={item}
          linhas={linhas}
          aoEditar={aoEditar}
          adicionais={adicionaisDoItem(cardapio, adicionais, item.sku)}
        />}
    </li>
  )
}

// Papel 1: anotar. Um toque soma na comanda, sem passo intermediário. Item
// esgotado continua vendendo (D7 e RN-48), item fora do dia não: quem desligou
// foi ela, de propósito. O prato também se arrasta até a comanda (seção 9);
// o toque continua somando, e é o caminho mais rápido.
function rotuloDoEnvio(envioDeFoto, item) {
  if (envioDeFoto.enviando === item.sku) return 'Enviando a foto de ' + item.nome
  if (envioDeFoto.enviado === item.sku) return 'Foto de ' + item.nome + ' enviada'
  return 'Enviar a foto de ' + item.nome + ' ao cliente'
}

// Grupos pela categoria da vitrine (#1448): 43 itens em lista corrida não se
// leem. O título do grupo é texto, sem caixa: a hierarquia vem da tipografia.
// Cardápio sem categoria nenhuma (a massa de demonstração) é uma lista só, sem título "Outros".
function PorCategoria({ itens, children }) {
  const grupos = agruparPorCategoria(itens)
  if (grupos.length === 1 && grupos[0].categoria === SEM_CATEGORIA) return children(grupos[0].itens)
  return grupos.map((grupo) => (
    <section key={grupo.categoria} className={css.grupo} aria-label={grupo.categoria}>
      <h3 className={css.categoria}>
        {grupo.categoria} <span>{grupo.itens.length}</span>
      </h3>
      {children(grupo.itens)}
    </section>
  ))
}

function Escolher({
  cardapio, visiveis, linhas, adicionais, pedido, agora, aoEscolher, aoAjustar, envioDeFoto, podeGerir, aoEditar, travada,
}) {
  const naComanda = (sku) => pedido?.itens.find((l) => l.sku === sku)?.qtd ?? 0
  const mostrar = (item, compacto = false) => {
    const situacao = situacaoDoItem(item)
    return <ItemEscolher key={item.sku} item={item} situacao={situacao}
      quantos={naComanda(item.sku)} compacto={compacto}
      alternativas={situacao.chave === 'esgotado' || situacao.chave === 'fora-do-dia' ? alternativasPara(cardapio, item.sku) : []}
      linhas={linhas} adicionais={adicionais} cardapio={cardapio} novidade={ehNovidade(item, agora)}
      aoEscolher={aoEscolher} aoAjustar={aoAjustar} envioDeFoto={envioDeFoto} podeGerir={podeGerir}
      naComanda={naComanda} aoEditar={aoEditar} travada={travada} />
  }

  return (
    <PorCategoria itens={visiveis}>
      {(itens) => (
        <ul className={css.lista}>
          {agruparApresentacoes(itens).map((grupo) => grupo.itens.length === 1 ? mostrar(grupo.itens[0]) : (
            <li key={grupo.itens[0].sku} className={css.receita}>
              <div className={css.cabecalhoReceita}>
                <h4>{grupo.nome}</h4>
              </div>
              <ul className={css.tamanhos} aria-label={`Apresentações de ${grupo.nome}`}>
                {grupo.itens.map(item => mostrar(item, true))}
              </ul>
            </li>
          ))}
        </ul>
      )}
    </PorCategoria>
  )
}

// Papel 2: gerir o dia. Duas coisas diferentes, separadas de propósito: o saldo
// é quanto tem no congelador, a disponibilidade é se a casa vende isso hoje.
// Gerir o dia (US-020): além de saldo e disponibilidade (rodada 2), a dona
// inclui item, edita, marca novidade e tira do cardápio sem apagar
// histórico. RN-15: item recém-criado nasce em validação, com "Confirmar" e
// "Tirar" lado a lado — a decisão é sempre dela, nunca automática.
function Gerir({
  cardapio, removidos, linhas, adicionais, agora, aoAlternar, aoAjustar,
  aoAbrirNovo, aoAbrirEditar, aoTirar, aoRepor, aoConfirmarValidacao,
  podeEditar,
}) {
  return (
    <div className={css.gerir}>
      {podeEditar && <Botao variante="secundario" icone="mais" className={css.botaoNovoItem} onClick={aoAbrirNovo}>
        Incluir item novo
      </Botao>}

      <PorCategoria itens={cardapio}>
        {(itens) => (
          <ul className={css.gestao}>
            {itens.map((item) => {
              const situacao = situacaoDoItem(item)
              const novidade = ehNovidade(item, agora)
              return (
                <li key={item.sku} className={`${css.cartaoItem} ${situacao.vendavel ? '' : css.apagado}`}>
                  <FotoDoItem item={item} className={css.miniatura} />

                  <div className={css.corpoItem}>
                    <strong>
                      {item.nome}
                      {novidade && <Pilula tom="marca" fina>Novidade</Pilula>}
                      {estaEmValidacao(item) && <Pilula tom="aviso" fina>Em validação</Pilula>}
                    </strong>
                    <small>
                      {linhas[item.linha]?.rotulo} · {item.porcao} · {moeda(item.preco)}
                      {PEDE_ACAO.has(situacao.chave) && (
                        <b className={css[situacao.tom]}> · {situacao.rotulo}</b>
                      )}
                    </small>
                    <small className={css.extras}>
                      {adicionaisDoItem(cardapio, adicionais, item.sku).map((a) => a.nome).join(' · ')
                        || 'Sem adicional'}
                    </small>
                    {podeEditar && estaEmValidacao(item) && (
                      <p className={css.avisoValidacao}>
                        Vendeu menos de duas vezes. Confirme se ele fica no cardápio ou tire.
                        <button type="button" className={css.linkValidacao} onClick={() => aoConfirmarValidacao(item.sku)}>
                          Confirmar no cardápio
                        </button>
                      </p>
                    )}
                  </div>

                  <div className={css.controles}>
                    <label className={css.chave}>
                      <input
                        type="checkbox"
                        checked={situacao.chave !== 'fora-do-dia'}
                        onChange={() => aoAlternar(item.sku)}
                      />
                      Hoje
                    </label>
                    {item.estoque != null ? <span className={css.saldo}>
                      <button
                        type="button"
                        onClick={() => aoAjustar(item.sku, -1)}
                        disabled={item.estoque === 0}
                      >
                        <Icone nome="menos" rotulo={'Tirar uma porção de ' + item.nome} />
                      </button>
                      <b>{item.estoque}</b>
                      <button type="button" onClick={() => aoAjustar(item.sku, 1)}>
                        <Icone nome="mais" rotulo={'Somar uma porção de ' + item.nome} />
                      </button>
                    </span> : <span className={css.semSaldo}>Sem controle de saldo</span>}
                    {podeEditar && <span className={css.acoesItem}>
                      <button type="button" onClick={() => aoAbrirEditar(item)}>
                        <Icone nome="lapis" rotulo={'Editar ' + item.nome} />
                      </button>
                      <button type="button" onClick={() => aoTirar(item.sku)}>
                        <Icone nome="x" rotulo={'Tirar ' + item.nome + ' do cardápio'} />
                      </button>
                    </span>}
                  </div>
                </li>
              )
            })}
          </ul>
        )}
      </PorCategoria>

      {podeEditar && removidos.length > 0 && (
        <details className={css.foraDoCardapio}>
          <summary>Fora do cardápio · {removidos.length}</summary>
          <ul className={css.gestao}>
            {removidos.map((item) => (
              <li key={item.sku} className={`${css.cartaoItem} ${css.apagado}`}>
                <FotoDoItem item={item} className={css.miniatura} />
                <div className={css.corpoItem}>
                  <strong>{item.nome}</strong>
                  <small>{linhas[item.linha]?.rotulo} · {item.porcao} · {moeda(item.preco)}</small>
                </div>
                <div className={css.controles}>
                  <Botao variante="texto" icone="undo-2" onClick={() => aoRepor(item.sku)}>
                    Repor no cardápio
                  </Botao>
                </div>
              </li>
            ))}
          </ul>
        </details>
      )}
    </div>
  )
}

// Rodapé fixo da comanda dentro do painel do cardápio (seção 3.1, pedido
// literal do dono: "quando adiciono um item do cardápio na comanda, eu não
// tenho uma visão do valor total nem da comanda"). Recolhido por padrão
// (total sempre à vista sem empurrar a lista de pratos), com "Ver itens" para
// conferir a comanda inteira sem fechar o cardápio. `aria-live` fala o nome
// do último prato somado porque quem está de olho na tela do cardápio,
// não na comanda, precisa ouvir a confirmação, não só ver o número mudar.
function RodapeComanda({ pedido, cardapio, linhas, nomeCliente }) {
  const { fonteApi } = useAtendimento()
  const [aberto, setAberto] = useState(false)
  const [eco, setEco] = useState('')
  const totalAnteriorRef = useRef(null)
  const [somaEmDestaque, setSomaEmDestaque] = useState(null)

  const itens = itensDetalhados(pedido, cardapio)
  // #2002 comanda-20: no pedido do EasyStok o total é o do servidor (frete e preço gravado), não a soma pelo cardápio de hoje.
  const total = totalDaVenda(pedido, cardapio)
  const qtdItens = itens.reduce((soma, linha) => soma + linha.qtd, 0)
  const grupos = agruparPorLinha(itens, linhas)
  // Mesmo cálculo de BlocoPedido.jsx (defeito c, decisão 23; a conta mora em
  // `dominio/pedido.js` pra não duplicar aqui e lá).
  const diferencaPosPagamento = diferencaAposPagamento(pedido, cardapio)

  useEffect(() => {
    const anterior = totalAnteriorRef.current
    totalAnteriorRef.current = total
    if (anterior == null || total <= anterior) return
    setSomaEmDestaque(total - anterior)
    const t = setTimeout(() => setSomaEmDestaque(null), 600)
    return () => clearTimeout(t)
  }, [total])

  useEffect(() => {
    const ultimo = itens.at(-1)
    if (!ultimo) return
    setEco(`${ultimo.produto?.nome ?? 'Item'} na comanda. Total ${moeda(total)}.`)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [qtdItens])

  if (qtdItens === 0) return null
  const numero = numeroDaComanda(pedido, { fonteApi })

  return (
    <div className={css.rodapeComanda}>
      <button
        type="button"
        className={css.rodapeResumo}
        aria-expanded={aberto}
        onClick={() => setAberto((v) => !v)}
      >
        <span className={css.rodapeTitulo}>
          {/* #1587: de quem é a comanda, sempre à vista (o painel não fecha ao trocar de conversa). */}
          Comanda{nomeCliente ? ` de ${nomeCliente}` : ''} · {numero ? `Nº ${numero}` : 'rascunho'} · {qtdItens} {qtdItens === 1 ? 'item' : 'itens'}
        </span>
        <span className={`${css.rodapeTotal} ${somaEmDestaque ? css.rodapeTotalSomando : ''}`}>
          {somaEmDestaque != null && <b className={css.rodapeSoma}>+ {moeda(somaEmDestaque)}</b>}
          {moeda(total)}
        </span>
        <Icone nome="chevron-up" tamanho={20} rotulo={aberto ? 'Recolher itens da comanda' : 'Ver itens da comanda'} />
      </button>

      {diferencaPosPagamento > 0 && (
        <p className={css.rodapeAcrescimo} role="alert">
          <Icone nome="alerta" /> Acréscimo a cobrar <b>{moeda(diferencaPosPagamento)}</b>
        </p>
      )}

      {aberto && (
        <div className={css.rodapeItens}>
          {grupos.map((grupo) => (
            <div key={grupo.chave}>
              <span className={css.rotuloGrupo}>{grupo.rotulo}</span>
              <ul>
                {grupo.itens.map((linha) => (
                  <li key={linha.sku}>
                    <span>{linha.qtd}× {linha.produto?.nome}</span>
                    <span>{moeda((linha.produto?.preco ?? 0) * linha.qtd)}</span>
                    {linha.obs && <span className={css.rodapeObs}>{linha.obs}</span>}
                  </li>
                ))}
              </ul>
            </div>
          ))}
        </div>
      )}

      <p className="sr" aria-live="polite">{eco}</p>
    </div>
  )
}

// Cardápio como painel lateral, ancorado à Ficha (seção 9: "o 'janela' da fala
// do dono"). Deixa de tampar a tela porque ela consulta o cardápio enquanto lê
// a conversa: sem cortina, sem <dialog>, o resto da tela continua clicável.
export function PainelCardapio({
  pedido, aoEscolher, aoAjustar, aoFechar, largura: larguraGuardada, ladoDoBalcao = false, aoRedimensionar,
  larguraMaxima = 600, amplo = false, aoAlternarAmplo, aoTrocarLado,
}) {
  // #1474: largura guardada de antes (320 a 400) espremia o nome do prato; o mínimo agora é 440.
  const largura = Math.max(larguraGuardada ?? LARGURA_MINIMA, LARGURA_MINIMA)
  const { cardapio, linhas, adicionais, canais } = useCatalogo()
  const { agora, selecionada, fonteApi } = useAtendimento()
  const {
    alternarDisponibilidade, ajustarSaldo,
    alternarRemocaoItemCardapio, confirmarValidacaoItem, enviarMidia,
  } = useAcoes()
  const [papel, setPapel] = useState('escolher')
  // #1474 (R2): a aba "Gerir o dia" só aparece quando a ação está ligada (#1241 ligou no modo API);
  // ação não ligada some em vez de só avisar.
  const podeGerir = useAcaoDisponivel()('alternarDisponibilidade')
  const acesso = useAcessoModulos()
  const podeEditar = !fonteApi || acesso.acoes.editarCardapio === true
  const papeis = podeGerir ? PAPEIS : PAPEIS.filter((p) => p.id !== 'gerir')
  const [modal, setModal] = useState(null)
  const [busca, setBusca] = useState('')
  const [enviandoSku, setEnviandoSku] = useState(null)
  const [enviadoSku, setEnviadoSku] = useState(null)
  // Falha do envio de foto (#1587), com a conversa em que aconteceu: não segue para a próxima.
  const [falhaFoto, setFalhaFoto] = useState(null)
  // #1587: o painel não é modal e o "+" soma na conversa selecionada NAQUELE instante. O nome
  // do cliente fica no cabeçalho e no rodapé, para a troca de conversa não passar em silêncio.
  const nomeCliente = selecionada?.nome ? primeiroNome(selecionada.nome) : ''
  const travada = comandaTravada(pedido, { fonteApi })
  const fecharRef = useRef(null)
  const buscaRef = useRef(null)
  const focoAnterior = useRef(null)

  useEscape(!modal, aoFechar)

  useEffect(() => {
    focoAnterior.current = document.activeElement
    const focoInicial = buscaRef.current ?? fecharRef.current
    focoInicial?.focus()
    return () => {
      if (focoAnterior.current?.isConnected) focoAnterior.current.focus()
      else {
        const comanda = document.getElementById('comanda-pedido')
        comanda?.focus({ preventScroll: true })
        comanda?.scrollIntoView({ block: 'start' })
      }
    }
  }, [])

  const temPedido = Boolean(pedido)
  useEffect(() => {
    if (!temPedido) return
    // Seção 3.1: abrir o cardápio rola a Ficha até a comanda, pra ela ficar
    // ao lado do painel em vez de escondida acima da dobra. `id` compartilhado
    // por atributo (não por import) porque cardápio e ficha-cliente são
    // features separadas — a fronteira de camadas não deixa uma importar a
    // outra, então o encontro é só no DOM, como o resto do arrasto já faz.
    // #1442: a comanda pode estar recolhida na Ficha; abre e rola depois que
    // ela volta ao layout.
    abrirSecao('comanda')
    const reduzMovimento = window.matchMedia('(prefers-reduced-motion: reduce)').matches
    const rolar = setTimeout(() => document.getElementById('comanda-pedido')
      ?.scrollIntoView({ behavior: reduzMovimento ? 'auto' : 'smooth', block: 'nearest' }))
    return () => {
      clearTimeout(rolar)
    }
  }, [temPedido])

  const ativos = itensAtivos(cardapio)
  const removidos = itensRemovidos(cardapio)
  const noDia = contarDisponiveis(ativos)
  const visiveis = filtrarCardapio(ativos, busca)

  // Mesma regra da galeria (PainelGaleria.jsx): canal que aceita foto, janela aberta e, no modo
  // API, só o WhatsApp tem envio de foto. O motivo vai no title do botão desabilitado.
  const canal = canalDaConversa(canais, selecionada)
  const escrita = permissaoDeEscrita(selecionada, agora, canal)
  let motivoFoto = null
  if (!escrita.pode) motivoFoto = escrita.motivo?.detalhe ?? 'Conversa sem envio agora.'
  else if (!aceitaFormato(canal, 'foto')) motivoFoto = motivoDeFormato(canal, 'foto')
  else if (fonteApi && selecionada?.canal !== 'WhatsApp') motivoFoto = `${selecionada?.canal} ainda não recebe foto pelo EasyStok.`

  async function enviarFoto(item) {
    const mensagem = mensagemDaFotoDoItem(item)
    if (!mensagem || !selecionada) return
    setEnviandoSku(item.sku)
    setFalhaFoto(null)
    const enviado = await enviarMidia(selecionada.id, mensagem)
    setEnviandoSku(null)
    if (enviado === false) { setFalhaFoto({ sku: item.sku, conversaId: selecionada.id }); return }
    setEnviadoSku(item.sku)
    setTimeout(() => setEnviadoSku((atual) => (atual === item.sku ? null : atual)), 1400)
  }

  const envioDeFoto = selecionada
    ? {
      pode: !motivoFoto, motivo: motivoFoto, enviando: enviandoSku, enviado: enviadoSku, enviar: enviarFoto,
      falhou: falhaFoto?.conversaId === selecionada.id ? falhaFoto.sku : null,
    }
    : null

  return (
    <aside
      className={`${css.painel} ${ladoDoBalcao ? css.painelDoBalcao : ''}`}
      aria-label="Cardápio de hoje"
      style={{ width: `${largura}px` }}
    >
      <AlcaLargura
        rotulo="Redimensionar painel do cardápio"
        valor={largura}
        min={LARGURA_MINIMA}
        max={larguraMaxima}
        padrao={Math.min(460, larguraMaxima)}
        invertida={!ladoDoBalcao}
        aoMudar={aoRedimensionar}
      />

      <div className={css.corpoPainel}>
        <header className={css.cabecalhoPainel}>
          <div>
            <h2>{travada && papel === 'escolher' ? 'Consulta do cardápio' : 'Cardápio de hoje'}</h2>
            {nomeCliente && <p className={css.donoDaComanda} aria-live="polite">Comanda de <b>{nomeCliente}</b></p>}
            <p className={css.subPainel}>{noDia} de {ativos.length} itens no dia.</p>
            {papel === 'escolher' && !travada && <p className={css.subPainel}>Busque um prato e escolha a porção para adicionar à comanda.</p>}
          </div>
          <div className={css.ferramentasPainel}>
            {/* #1806: lado e tamanho do painel, lembrados por aparelho. Só ícone para caber nos
                440 px do painel ao lado do título; o nome vai no aria-label e no title. */}
            {aoTrocarLado && (
              <button
                type="button"
                className={css.fechar}
                aria-label={`Mover o cardápio para a ${ladoDoBalcao ? 'direita, sobre o Resumo' : 'esquerda, sobre o Balcão'}`}
                title={ladoDoBalcao ? 'Mover para a direita' : 'Mover para a esquerda'}
                onClick={aoTrocarLado}
              >
                <Icone nome="trocarLado" />
              </button>
            )}
            {aoAlternarAmplo && (
              <button
                type="button"
                className={css.fechar}
                aria-label={`${amplo ? 'Reduzir' : 'Ampliar'} o cardápio`}
                title={amplo ? 'Reduzir' : 'Ampliar'}
                onClick={aoAlternarAmplo}
              >
                <Icone nome={amplo ? 'reduzir' : 'ampliar'} />
              </button>
            )}
            <button type="button" ref={fecharRef} className={css.fechar} aria-label="Fechar cardápio" title="Fechar cardápio" onClick={aoFechar}>
              <Icone nome="fechar" />
            </button>
          </div>
        </header>

        {/* Botão de alternância em vez do padrão ARIA de aba: aba promete
            navegação por seta, e prometer teclado que não existe é pior que
            não prometer. */}
        {papeis.length > 1 && (
          <fieldset className={css.papeis}>
            <legend className="sr">O que fazer no cardápio</legend>
            {papeis.map((p) => (
              <button
                type="button"
                key={p.id}
                aria-pressed={papel === p.id}
                className={`${css.papel} ${papel === p.id ? css.papelAtivo : ''}`}
                onClick={() => setPapel(p.id)}
              >
                {p.id === 'escolher' && travada ? 'Consultar pratos' : p.rotulo}
              </button>
            ))}
          </fieldset>
        )}

        {ativos.length > 0 && (
          <label className={css.busca}>
            <Icone nome="lupa" />
            <span className="sr">Buscar no cardápio</span>
            <input
              ref={buscaRef}
              type="search"
              value={busca}
              onChange={(e) => setBusca(e.target.value)}
              placeholder="Buscar prato, categoria ou porção"
            />
          </label>
        )}

        <div className={css.rolavelPainel}>
          {ativos.length > 0 && visiveis.length === 0 && (
            <p className={css.semResultado}>Nada com &ldquo;{busca.trim()}&rdquo; no cardápio de hoje.</p>
          )}

          {ativos.length === 0 && (
            <Vazio
              titulo="Cardápio sem itens"
              acao={podeEditar && <Botao variante="primario" onClick={() => setModal({ modo: 'novo' })}>Incluir item novo</Botao>}
            >
              Nenhum item no cardápio de hoje. Sem cardápio não dá para anotar comanda
              nem dizer o que a casa vende hoje.
            </Vazio>
          )}

          {ativos.length > 0 && papel === 'escolher' && (
            <Escolher
              cardapio={ativos}
              visiveis={visiveis}
              linhas={linhas}
              adicionais={adicionais}
              pedido={pedido}
              agora={agora}
              aoEscolher={aoEscolher}
              aoAjustar={aoAjustar}
              envioDeFoto={envioDeFoto}
              podeGerir={podeGerir}
              aoEditar={podeEditar ? (item) => setModal({ modo: 'editar', item }) : undefined}
              travada={travada}
            />
          )}

          {papel === 'gerir' && podeGerir && (
            <Gerir
              podeEditar={podeEditar}
              cardapio={visiveis}
              removidos={removidos}
              linhas={linhas}
              adicionais={adicionais}
              agora={agora}
              aoAlternar={alternarDisponibilidade}
              aoAjustar={ajustarSaldo}
              aoAbrirNovo={() => setModal({ modo: 'novo' })}
              aoAbrirEditar={(item) => setModal({ modo: 'editar', item })}
              aoTirar={(sku) => alternarRemocaoItemCardapio(sku, agora)}
              aoRepor={(sku) => alternarRemocaoItemCardapio(sku, agora)}
              aoConfirmarValidacao={confirmarValidacaoItem}
            />
          )}
        </div>

        {/* `cardapio` cheio (não só `ativos`) de propósito: um item já anotado
            na comanda continua precificando certo mesmo depois de tirado do
            cardápio (RN de não apagar histórico). */}
        {/* #1587: pedido já criado não soma nem tira por aqui; o motivo fica escrito acima do total. */}
        {papel === 'escolher' && travada && <output className={css.comandaTravada}>{LINHA_COMANDA_TRAVADA}</output>}
        {papel === 'escolher' && pedido && <RodapeComanda pedido={pedido} cardapio={cardapio} linhas={linhas} nomeCliente={nomeCliente} />}
      </div>

      {modal && (
        <EditorItemCardapio
          item={modal.modo === 'editar' ? modal.item : null}
          aoFechar={() => setModal(null)}
        />
      )}
    </aside>
  )
}
