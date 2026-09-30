import { useEffect, useRef, useState } from 'react'
import { useDndContext, useDroppable } from '@dnd-kit/core'
import { Botao } from '../../componentes/Botao'
import { CampoArea } from '../../componentes/Campo'
import { Icone } from '../../componentes/Icone'
import { Pilula } from '../../componentes/Pilula'
import { useAcoes, useCatalogo } from '../../aplicacao/contextos'
import { moeda } from '../../dominio/formato'
import {
  agruparPorLinha, itensDetalhados, numeroCurto, pedidoEncerrado, totalDoPedido,
} from '../../dominio/pedido'
import { diferencaAposPagamento, totalPago } from '../../dominio/pagamento'
import { situacaoDoItem } from '../../dominio/cardapio'
import { ROTULO_ENVIAR, avisoDoEnvio, situacaoDoEnvio } from '../../dominio/resumoPedido'
import { Cabecalho } from './Cabecalho'
import { ModalCanhoto } from './Canhoto'
import { SeletorJanela } from './SeletorJanela'
import { EscolhaDeMeio } from './EscolhaDeMeio'
import css from './comanda.module.css'

// Só o que pede ação aparece na comanda: esgotado e fora do dia. Saldo curto já
// tem lugar no cardápio, e pílula demais é o que transforma a ficha em mancha.
const PRECISA_DE_MARCA = new Set(['esgotado', 'fora-do-dia'])

function MarcaDoItem({ produto }) {
  if (!produto) return null
  const situacao = situacaoDoItem(produto)
  if (!PRECISA_DE_MARCA.has(situacao.chave)) return null
  return <Pilula tom={situacao.tom} fina>{situacao.rotulo}</Pilula>
}

function ItemComanda({
  linha, fechado, observacaoTravada, obsAberta, aoAlternarObs, aoAjustar, aoAjustarObservacao,
}) {
  // Seção 3.3, gesto novo: toque em qualquer lugar do nome abre a anotação,
  // não só no lápis de 48 px — alvo do tamanho da linha inteira, não de um
  // ícone. O lápis continua ao lado do nome, mas só como sinal visual
  // (`aria-hidden`); quem lê por teclado ou leitor de tela usa o rótulo do
  // próprio botão do nome.
  const podeAnotar = !observacaoTravada
  const corpo = (
    <>
      <span className={css.nomeItem}>
        {linha.produto?.nome}
        <MarcaDoItem produto={linha.produto} />
        {podeAnotar && <Icone nome="lapis" tamanho={16} />}
      </span>
      <span className={css.porcao}>{linha.produto?.porcao}</span>
    </>
  )
  return (
    <li className={css.item}>
      <div className={css.linhaItem}>
        <span className={css.qtd}>{linha.qtd}×</span>
        {podeAnotar ? (
          <button
            type="button"
            className={`${css.corpo} ${css.corpoAnotar}`}
            aria-pressed={obsAberta}
            aria-label={`Anotar na ${linha.produto?.nome}`}
            onClick={aoAlternarObs}
          >
            {corpo}
          </button>
        ) : (
          <span className={css.corpo}>{corpo}</span>
        )}
        {fechado
          ? <span className={css.preco}>{moeda((linha.produto?.preco ?? 0) * linha.qtd)}</span>
          : (
            <span className={css.controlesItem}>
              <span className={css.contador}>
                <button type="button" aria-label="Menos um" onClick={() => aoAjustar(linha.sku, -1)}>
                  <Icone nome="menos" rotulo={`Tirar uma unidade de ${linha.produto?.nome}`} />
                </button>
                <b>{linha.qtd}</b>
                <button type="button" aria-label="Mais um" onClick={() => aoAjustar(linha.sku, 1)}>
                  <Icone nome="mais" rotulo={`Mais uma unidade de ${linha.produto?.nome}`} />
                </button>
              </span>
            </span>
          )}
      </div>
      {!observacaoTravada && obsAberta ? (
        <CampoArea
          rotulo={`Observação de ${linha.produto?.nome}`}
          rotuloOculto
          className={css.obsCampo}
          rows={1}
          placeholder="Observação: sem queijo, molho à parte..."
          value={linha.obs}
          onChange={(e) => aoAjustarObservacao(linha.sku, e.target.value)}
          onBlur={aoAlternarObs}
        />
      ) : (
        linha.obs && <span className={css.obs}>{linha.obs}</span>
      )}
    </li>
  )
}

export function BlocoPedido({
  pedido, editavel, aoTrocarJanela, aoAbrirCardapio, aoAjustar, aoAjustarObservacao, aoGerarPedido,
  aoForcarEncaixe, nomeCliente, endereco, faixa, bloqueado = false,
}) {
  const { cardapio, linhas } = useCatalogo()
  const { marcarCanhotoImpresso } = useAcoes()
  const [canhotoAberto, setCanhotoAberto] = useState(false)
  // Ponta (a): "Enviar comanda" sem meio marcado pergunta o meio antes de
  // mandar, em vez de mandar Pix por baixo.
  const [pedindoMeio, setPedindoMeio] = useState(false)
  // Rodada 12 (issue #14): o envio precisa de retorno na ficha, não só na
  // conversa. O aviso nasce quando a cobrança que o clique pediu aparece no
  // pedido, então só diz "enviada" o que o reducer aceitou de verdade.
  const [aviso, setAviso] = useState(null)
  const esperandoEnvio = useRef(false)
  const cobrancaEnviada = pedido.cobranca ?? null
  const primeiroNome = (nomeCliente ?? '').split(' ')[0]
  useEffect(() => {
    if (!esperandoEnvio.current || !cobrancaEnviada) return undefined
    esperandoEnvio.current = false
    setAviso(avisoDoEnvio(cobrancaEnviada, primeiroNome))
    const id = setTimeout(() => setAviso(null), 6000)
    return () => clearTimeout(id)
  }, [cobrancaEnviada, primeiroNome])
  // Sku com o campo de observação aberto no toque (seção 7: "o campo abre no
  // toque, não fica sempre à vista").
  const [obsAbertas, setObsAbertas] = useState(() => new Set())
  const fechado = pedidoEncerrado(pedido) || !editavel

  // Soltar aqui (seção 9): só acende quando o que está sendo arrastado é um
  // prato do cardápio. useDndContext em vez de prop: BlocoPedido não muda de
  // assinatura, quem monta (PainelFicha, da Frente A) não precisa saber de
  // arrasto nenhum.
  const { active, over } = useDndContext()
  const arrastandoPrato = active?.data?.current?.type === 'prato'
  const alvoDoArrasto = arrastandoPrato && over?.id === 'comanda-solta'
  const { setNodeRef: soltarRef } = useDroppable({
    id: 'comanda-solta', data: { type: 'comanda' }, disabled: fechado,
  })
  // A mensagem com a observação já saiu assim que a comanda foi mandada
  // (mesmo sinal que o resto da tela usa para "já mandou": `pedido.cobranca`
  // nasce em GERAR_PEDIDO). Reescrever o campo depois disso não reenvia nada,
  // e a ficha passava a divergir do que o cliente já leu (achado QA5).
  const observacaoTravada = fechado || Boolean(pedido.cobranca)
  const envio = situacaoDoEnvio(pedido, { bloqueado })
  const podeGerar = envio.pode
  const enviar = (meio) => { esperandoEnvio.current = true; aoGerarPedido(meio) }
  const itens = itensDetalhados(pedido, cardapio)
  const total = totalDoPedido(pedido, cardapio)
  const grupos = agruparPorLinha(itens, linhas)
  // Defeito c (achado do arquiteto, 23/09/2026): pedido pago que ganha item
  // novo não fica calado sobre quanto falta cobrar (`dominio/pedido.js` tem
  // o porquê da conta e a lacuna conhecida de um segundo acréscimo).
  const diferencaPosPagamento = diferencaAposPagamento(pedido, cardapio)

  const alternarObs = (sku) => setObsAbertas((atual) => {
    const novo = new Set(atual)
    if (novo.has(sku)) novo.delete(sku); else novo.add(sku)
    return novo
  })

  return (
    <>
      <section
        ref={soltarRef}
        id="comanda-pedido"
        className={`${css.comanda} ${alvoDoArrasto ? css.alvoArrasto : ''}`}
        aria-label={`Comanda número ${numeroCurto(pedido.numero)}`}
      >
        <Cabecalho
          numero={pedido.numero}
          nomeCliente={nomeCliente}
          faixa={faixa}
          enviadaEm={pedido.cobranca?.criadaEm ?? null}
        />
        <hr className={css.separador} />

        {alvoDoArrasto && <p className={css.soltarAqui}>Soltar aqui</p>}

        {grupos.length === 0 && (
          <p className={css.vazio}>Comanda vazia. Abra o cardápio para montar o pedido.</p>
        )}

        {grupos.map((grupo) => (
          <div key={grupo.chave} className={css.grupo}>
            <span className={css.rotuloGrupo}>{grupo.rotulo}</span>
            <ul className={css.itens}>
              {grupo.itens.map((linha) => (
                <ItemComanda
                  key={linha.sku}
                  linha={linha}
                  fechado={fechado}
                  observacaoTravada={observacaoTravada}
                  obsAberta={obsAbertas.has(linha.sku)}
                  aoAlternarObs={() => alternarObs(linha.sku)}
                  aoAjustar={aoAjustar}
                  aoAjustarObservacao={aoAjustarObservacao}
                />
              ))}
            </ul>
          </div>
        ))}

        <hr className={css.separador} />
        <p className={css.total}>
          <span>Total</span>
          <b>{moeda(total)}</b>
        </p>
        {diferencaPosPagamento > 0 && (
          <p className={css.diferencaPaga} role="alert">
            <Icone nome="alerta" />
            Pago {moeda(totalPago(pedido))}. Item novo depois do pagamento:{' '}
            <b>{moeda(diferencaPosPagamento)}</b> a cobrar.
          </p>
        )}
      </section>

      <div className={css.acoesPedido}>
        {!fechado && (
          <>
            <Botao largo onClick={aoAbrirCardapio}>Abrir cardápio</Botao>
            {/* Rodada 12 (issue #14): sem botão morto. Ou ele envia, ou a
                linha diz por que não; comanda vazia já se explica no papel. */}
            {(podeGerar || envio.chave === 'vazia') ? (
              <Botao
                largo variante="primario" icone="enviar" disabled={!podeGerar}
                onClick={() => (pedido.meio ? enviar(pedido.meio) : setPedindoMeio(true))}
              >
                {ROTULO_ENVIAR}
              </Botao>
            ) : (
              <p className={`${css.situacaoEnvio} ${envio.chave === 'enviada' ? css.envioFeito : ''}`}>
                <Icone nome={envio.chave === 'enviada' ? 'check' : 'alerta'} />
                {envio.texto}
              </p>
            )}
            {pedindoMeio && podeGerar && !pedido.meio && (
              <EscolhaDeMeio
                escolhido={null}
                aoEscolher={(meio) => { setPedindoMeio(false); enviar(meio) }}
                pergunta="Como o cliente vai pagar? A comanda sai com a cobrança."
              />
            )}
          </>
        )}
        {itens.length > 0 && (
          <Botao largo icone="printer" onClick={() => setCanhotoAberto(true)}>Ver canhoto</Botao>
        )}
      </div>

      {aviso && (
        <output className={css.avisoEnvio} aria-live="polite">
          <Icone nome="check" />
          <span>{aviso}</span>
        </output>
      )}

      {canhotoAberto && (
        <ModalCanhoto
          pedido={pedido}
          itens={itens}
          linhas={linhas}
          nomeCliente={nomeCliente}
          endereco={endereco}
          faixa={faixa}
          aoFechar={() => setCanhotoAberto(false)}
          aoImprimir={() => marcarCanhotoImpresso(pedido.numero)}
        />
      )}

      {!fechado && (
        <SeletorJanela
          pedido={pedido}
          editavel={editavel}
          aoEscolher={aoTrocarJanela}
          aoForcarEncaixe={aoForcarEncaixe}
        />
      )}
    </>
  )
}
