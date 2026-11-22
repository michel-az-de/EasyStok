import { useEffect, useRef, useState } from 'react'
import { useDicionarioDaLoja, useHistoricoAssistente, usePainelAssistente } from '../../../aplicacao/usePainelAssistente'
import { BarraDeAbas } from '../../../componentes/BarraDeAbas'
import { Botao } from '../../../componentes/Botao'
import { CampoTexto } from '../../../componentes/Campo'
import { Pilula } from '../../../componentes/Pilula'
import {
  cotaDoCampo, cotaParaCampo, desfechoDoTurno, formatarTokens, linhaDoUso, resumoDoUso, semIaDoUso,
} from '../../../dominio/painelAssistente'
import { AbaDicionario } from './AbaDicionario'
import { AbaFluxo } from './AbaFluxo'
import { Retorno } from './Retorno'
import css from './painelAssistente.module.css'

const ABAS = [
  { id: 'uso', nome: 'Uso e cota', rotulo: 'Uso e cota' },
  { id: 'fluxo', nome: 'Fluxo', rotulo: 'Fluxo' },
  { id: 'historico', nome: 'Histórico', rotulo: 'Histórico' },
  { id: 'dicionario', nome: 'Dicionário', rotulo: 'Dicionário' },
]

// #2853: painel do Assistente na Gestão › Atendimento. Uso do mês contra a cota, o fluxo do turno como mapa
// (etapas fixas, instruções, roteiro e ferramentas, cada nó com o seu ajuste) e o histórico de turnos.
// Quem não administra a loja vê tudo e não muda nada.
// `abaAntes` ({ id, rotulo, conteudo }): aba extra na frente, aberta primeiro. O modal da conversa (#2855) põe
// ali a checagem da conversa; as outras abas são as mesmas da Gestão.
export function PainelAssistente({ podeEditar, abaAntes = null, sinalRecarregar = 0 }) {
  const [aba, setAba] = useState(abaAntes?.id ?? 'uso')
  const painel = usePainelAssistente()
  // #3149: o dicionário só é lido (GET de quem administra) na primeira vez que a aba abre, e não é relido ao voltar.
  const [dicionarioAberto, setDicionarioAberto] = useState(false)
  const dicionarioDaLoja = useDicionarioDaLoja(podeEditar && dicionarioAberto)
  const trocarAba = (id) => { setAba(id); if (id === 'dicionario') setDicionarioAberto(true) }
  const recarregar = painel.recarregar
  const ultimoSinal = useRef(sinalRecarregar)
  useEffect(() => {
    if (sinalRecarregar !== ultimoSinal.current) { ultimoSinal.current = sinalRecarregar; recarregar() }
  }, [sinalRecarregar, recarregar])
  const abas = abaAntes ? [{ id: abaAntes.id, nome: abaAntes.rotulo, rotulo: abaAntes.rotulo }, ...ABAS] : ABAS

  return (
    <section className={css.painel} data-contexto={abaAntes ? 'modal' : 'pagina'} {...(abaAntes ? { 'aria-label': 'Painel do Assistente' } : { 'aria-labelledby': 'painel-assistente-titulo' })}>
      {!abaAntes && (
        <div className={css.topo}>
          <h3 id="painel-assistente-titulo">Painel do Assistente</h3>
          <Botao icone="refresh-cw" onClick={painel.recarregar} disabled={painel.carregando}>Atualizar</Botao>
        </div>
      )}
      <BarraDeAbas rolavel className={css.abas} abas={abas} ativa={aba} aoTrocar={trocarAba} idPainel="painel-assistente-conteudo" prefixoId="painel-assistente" rotulo="Partes do painel do Assistente" />
      <div id="painel-assistente-conteudo" role="tabpanel" aria-labelledby={`painel-assistente-${aba}`} className={css.conteudo} aria-busy={painel.carregando || (aba === 'dicionario' && dicionarioDaLoja.carregando)}>
        {painel.erro && aba !== abaAntes?.id && aba !== 'dicionario' && <p role="alert" className={css.erro}>Não consegui ler o Assistente agora: {painel.erro}</p>}
        {!painel.uso && painel.carregando && aba !== abaAntes?.id && aba !== 'dicionario' && <output className={css.nota}>Carregando…</output>}
        {abaAntes && aba === abaAntes.id && abaAntes.conteudo}
        {aba === 'uso' && painel.uso && <AbaUso uso={painel.uso} podeEditar={podeEditar} painel={painel} />}
        {aba === 'fluxo' && painel.fluxo && <AbaFluxo fluxo={painel.fluxo} podeEditar={podeEditar} painel={painel} />}
        {aba === 'historico' && <AbaHistorico />}
        {aba === 'dicionario' && <AbaDicionario podeEditar={podeEditar} painel={painel} dicionarioDaLoja={dicionarioDaLoja} />}
      </div>
    </section>
  )
}

function AbaUso({ uso, podeEditar, painel }) {
  const resumo = resumoDoUso(uso)
  const semIa = semIaDoUso(uso)
  const [campo, setCampo] = useState(() => cotaParaCampo(uso.cotaTokensMes))
  const cota = cotaDoCampo(campo)
  const mudou = cota.valor !== null && cota.valor !== (Number(uso.cotaTokensMes) || 0)
  const largura = resumo.comCota ? Math.min(100, resumo.percentual) : 0

  return (
    <div className={css.uso}>
      <div className={`${css.consumo} ${css['consumo_' + resumo.tom]}`} data-tom={resumo.tom}>
        <p className={css.consumoTitulo}><strong>{resumo.titulo}</strong></p>
        {resumo.comCota && (
          <div className={css.barra} role="meter" aria-label="Consumo da cota do mês" aria-valuemin={0} aria-valuemax={100} aria-valuenow={Math.min(100, resumo.percentual)} aria-valuetext={`${resumo.percentual}% da cota`}>
            <span className={css.barraCheia} style={{ width: `${largura}%` }} />
            <span className={css.barraAlerta} aria-hidden="true" />
          </div>
        )}
        <p className={css.consumoDetalhe}>{linhaDoUso(uso)}</p>
        <p className={css.consumoDetalhe}>{semIa.titulo}{semIa.detalhe && ` · ${semIa.detalhe}`}</p>
      </div>

      <form className={css.cota} onSubmit={(e) => { e.preventDefault(); if (mudou) painel.salvar('cota', { cotaTokensMes: cota.valor }) }}>
        <CampoTexto
          rotulo="Cota mensal (mil tokens)" inputMode="numeric" value={campo} disabled={!podeEditar}
          onChange={(e) => setCampo(e.target.value)} erro={cota.erro}
          dica={podeEditar ? 'Vazio tira a cota. Em 100% passa para a equipe.' : 'Só quem administra a loja muda a cota.'}
        />
        {podeEditar && (
          <div className={css.acoes}>
            {mudou && <Botao variante="primario" tipo="submit" disabled={painel.gravacao.salvando === 'cota'}>Salvar cota</Botao>}
            <Retorno gravacao={painel.gravacao} qual="cota" />
          </div>
        )}
      </form>
    </div>
  )
}

function AbaHistorico() {
  const { carregando, dados, erro, pagina, setPagina } = useHistoricoAssistente(true)
  const total = dados?.total ?? 0
  const paginas = Math.max(1, Math.ceil(total / 20))
  if (erro) return <p role="alert" className={css.erro}>Não consegui ler o histórico: {erro}</p>
  if (!dados) return <output className={css.nota}>Carregando…</output>
  if (total === 0) return <p className={css.nota}>Nenhum turno do Assistente registrado ainda.</p>
  return (
    <div className={css.historico} aria-busy={carregando}>
      <ul className={css.turnos}>
        {dados.itens.map((t) => {
          const desfecho = desfechoDoTurno(t.desfecho)
          return (
            <li key={t.id} className={css.turno}>
              <div className={css.turnoTopo}>
                <Pilula tom={desfecho.tom} tamanho="inline">{desfecho.rotulo}</Pilula>
                <time dateTime={t.criadoEm}>{new Date(t.criadoEm).toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' })}</time>
                <span className={css.nota}>{formatarTokens(t.tokensEntrada + t.tokensSaida)} tokens · {(t.duracaoMs / 1000).toLocaleString('pt-BR', { maximumFractionDigits: 1 })} s</span>
              </div>
              {t.perguntaCliente && <p><strong>Cliente:</strong> {t.perguntaCliente}</p>}
              {t.resposta && <p><strong>Assistente:</strong> {t.resposta}</p>}
              {t.ferramentas?.length > 0 && <p className={css.nota}>Ferramentas: {t.ferramentas.map((f) => f.nome).join(', ')}</p>}
            </li>
          )
        })}
      </ul>
      {paginas > 1 && <div className={css.acoes}>
        <Botao icone="arrow-left" onClick={() => setPagina(pagina - 1)} disabled={pagina <= 1 || carregando}>Mais novos</Botao>
        <span className={css.nota}>Página {pagina} de {paginas}</span>
        <Botao onClick={() => setPagina(pagina + 1)} disabled={pagina >= paginas || carregando}>Mais antigos</Botao>
      </div>}
    </div>
  )
}
