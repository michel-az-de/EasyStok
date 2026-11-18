// #2832: painel "Agora" da Gestão da loja (Turno de hoje). Junta o que a loja usa no meio do atendimento sem sair
// da conversa: o cardápio do dia com Esgotou e Voltar, o caixa, os pedidos de hoje, o que precisa de você e as
// telas que você mais usa. Cada cartão só aparece para quem tem a tela dele; os números vêm da API e relêem ao vivo.
import { useMemo, useState } from 'react'
import { Botao } from '../../../componentes/Botao'
import { Icone } from '../../../componentes/Icone'
import { Pilula } from '../../../componentes/Pilula'
import { EsqueletoDeLista } from '../../../componentes/Esqueleto'
import { useAcoes, useAtendimento, useCatalogo } from '../../../aplicacao/contextos'
import { useAcaoDisponivel } from '../../../aplicacao/useAcaoDisponivel'
import { useCaixaDoDiaApi } from '../../../aplicacao/useCaixaDoDiaApi'
import { usePedidosDeHoje } from '../../../aplicacao/useTurnoDeHoje'
import { conversaEncerrada, estaBloqueada } from '../../../dominio/conversa'
import { moeda } from '../../../dominio/formato'
import {
  GRUPOS_DE_PEDIDOS, cardapioDoDia, maisUsadas, pendenciasDoTurno, recebidoPorForma, situacaoDoCaixa,
} from '../../../dominio/turnoDeHoje'
import css from './turno.module.css'

const TOM_DA_PILULA = { ok: 'ok', aviso: 'aviso', perigo: 'perigo', neutro: 'info' }

export function PainelTurnoDeHoje({ telas, uso, aoAbrirAba, renderizarEnviarCardapio = null }) {
  const abas = useMemo(() => new Set(telas.map((t) => t.aba)), [telas])
  const { cardapio } = useCatalogo()
  const { selecionada } = useAtendimento()
  const { alternarDisponibilidade } = useAcoes()
  const podeAlternar = useAcaoDisponivel()('alternarDisponibilidade')
  const comCardapio = podeAlternar && (abas.has('cardapio') || abas.has('estoque-do-dia'))
  const comCaixa = abas.has('caixa')
  const comPedidos = abas.has('cadastros-pedidos')
  const caixa = useCaixaDoDiaApi({ fechamentos: 1, ligado: comCaixa })
  const pedidos = usePedidosDeHoje({ ligado: comPedidos })
  const [enviando, setEnviando] = useState(false)

  const pendencias = pendenciasDoTurno({
    caixa: caixa.estado === 'ok' ? caixa.dia : null,
    pedidos: pedidos.estado === 'ok' ? pedidos.contagens : null,
    cardapio, abas,
  })
  const atalhos = maisUsadas(uso, telas)
  const conversaAberta = selecionada && !conversaEncerrada(selecionada) && !estaBloqueada(selecionada) ? selecionada : null

  return (
    <div className={css.turno}>
      <div className={css.grade}>
        {comCardapio && (
          <CartaoCardapio cardapio={cardapio} aoAlternar={alternarDisponibilidade}
            aoEnviar={conversaAberta && renderizarEnviarCardapio ? () => setEnviando(true) : null}
            nomeDaConversa={conversaAberta?.nome} />
        )}
        {(comCaixa || comPedidos) && (
          <div className={css.coluna}>
            {comCaixa && <CartaoCaixa carga={caixa} aoAbrir={() => aoAbrirAba('caixa')} />}
            {comPedidos && <CartaoPedidos carga={pedidos} aoAbrir={() => aoAbrirAba('cadastros-pedidos')} />}
          </div>
        )}
        <CartaoPendencias pendencias={pendencias} aoAbrirAba={aoAbrirAba} />
      </div>
      {atalhos.length > 0 && (
        <nav className={css.atalhos} aria-label="Você mais usa">
          <span className={css.rotuloAtalhos}>Você mais usa</span>
          {atalhos.map((tela) => (
            <button key={tela.aba} type="button" className={css.atalho} onClick={() => aoAbrirAba(tela.aba)}>{tela.rotulo}</button>
          ))}
        </nav>
      )}
      {enviando && conversaAberta && renderizarEnviarCardapio({ conversa: conversaAberta, aoFechar: () => setEnviando(false) })}
    </div>
  )
}

function Cartao({ icone, titulo, extra = null, children }) {
  return (
    <section className={css.cartao} aria-label={titulo}>
      <header className={css.cabecalho}>
        <Icone nome={icone} tamanho={20} />
        <h3 className={css.titulo}>{titulo}</h3>
        {extra}
      </header>
      {children}
    </section>
  )
}

function CartaoCardapio({ cardapio, aoAlternar, aoEnviar, nomeDaConversa }) {
  const { itens, noAr } = cardapioDoDia(cardapio)
  return (
    <Cartao icone="folha-cardapio" titulo="Cardápio do dia" extra={<span className={css.resumo}>{noAr} de {itens.length} no ar</span>}>
      {itens.length === 0 ? <p className={css.vazio}>Nenhum prato no cardápio.</p> : (
        <ul className={css.lista}>
          {itens.map((item) => (
            <li key={item.sku} className={css.linhaPrato}>
              <span className={css.nomePrato}>
                <strong>{item.nome}</strong>
                <Pilula tom={TOM_DA_PILULA[item.noAr ? item.tom : 'neutro']} tamanho="inline">{item.rotulo}</Pilula>
              </span>
              <Botao variante={item.noAr ? 'secundario' : 'primario'} onClick={() => aoAlternar(item.sku)}
                aria-label={`${item.noAr ? 'Marcar como esgotado' : 'Voltar ao cardápio'}: ${item.nome}`}>
                {item.noAr ? 'Esgotou' : 'Voltar'}
              </Botao>
            </li>
          ))}
        </ul>
      )}
      {aoEnviar && (
        <Botao variante="primario" icone="cardapio" largo onClick={aoEnviar}>
          Enviar cardápio para {nomeDaConversa || 'esta conversa'}
        </Botao>
      )}
    </Cartao>
  )
}

function CartaoCaixa({ carga, aoAbrir }) {
  const dia = carga.estado === 'ok' ? carga.dia : null
  const situacao = situacaoDoCaixa(dia)
  const formas = recebidoPorForma(dia)
  return (
    <Cartao icone="dollar-sign" titulo="Caixa do dia" extra={dia && <Pilula tom={TOM_DA_PILULA[situacao.tom]}>{situacao.rotulo}</Pilula>}>
      {carga.estado === 'carregando' && <EsqueletoDeLista linhas={2}>Lendo o caixa…</EsqueletoDeLista>}
      {carga.estado === 'erro' && !dia && <p className={css.vazio} role="alert">Não foi possível ler o caixa. {carga.erro}</p>}
      {dia && (
        <>
          <p className={css.valor}><strong>{moeda(dia.saldoEsperado)}</strong> <span>saldo esperado</span></p>
          {formas.length > 0 ? (
            <dl className={css.formas}>
              {formas.map((f) => <div key={f.metodo}><dt>{f.nome}</dt><dd>{moeda(f.valor)}</dd></div>)}
            </dl>
          ) : <p className={css.vazio}>Nada recebido hoje ainda.</p>}
        </>
      )}
      <Botao onClick={aoAbrir} largo>Abrir o Caixa do dia</Botao>
    </Cartao>
  )
}

function CartaoPedidos({ carga, aoAbrir }) {
  const pronto = carga.estado === 'ok' || carga.contagens
  return (
    <Cartao icone="shopping-cart" titulo="Pedidos de hoje" extra={pronto && <span className={css.resumo}>{carga.total} no total</span>}>
      {carga.estado === 'carregando' && !carga.contagens && <EsqueletoDeLista linhas={2}>Contando os pedidos…</EsqueletoDeLista>}
      {carga.estado === 'erro' && !carga.contagens && <p className={css.vazio} role="alert">Não foi possível contar os pedidos. {carga.erro}</p>}
      {carga.contagens && (
        <div className={css.montes}>
          {GRUPOS_DE_PEDIDOS.map((g) => (
            <button key={g.chave} type="button" className={`${css.monte} ${css[`monte_${g.tom}`] ?? ''}`} onClick={aoAbrir}>
              <strong>{carga.contagens[g.chave]}</strong><span>{g.rotulo}</span>
            </button>
          ))}
        </div>
      )}
    </Cartao>
  )
}

function CartaoPendencias({ pendencias, aoAbrirAba }) {
  return (
    <Cartao icone="alerta" titulo="Precisa de você"
      extra={pendencias.length > 0 && <Pilula tom="perigo" contagem={pendencias.length} titulo={`${pendencias.length} pendentes`} />}>
      {pendencias.length === 0 ? <p className={css.vazio}>Nada pendente agora.</p> : (
        <ul className={css.lista}>
          {pendencias.map((p) => (
            <li key={p.chave} className={`${css.pendencia} ${css[`pendencia_${p.tom}`] ?? ''}`}>
              <span className={css.nomePrato}><strong>{p.titulo}</strong><span>{p.detalhe}</span></span>
              <Botao onClick={() => aoAbrirAba(p.aba)}>{p.acao}</Botao>
            </li>
          ))}
        </ul>
      )}
    </Cartao>
  )
}
