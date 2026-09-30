import { useEffect, useRef, useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { Icone } from '../../componentes/Icone'
import { Pilula } from '../../componentes/Pilula'
import { MODOS, listaDeModos } from '../../dominio/modosDeConexao'
import css from './agente.module.css'

// O que a linha fechada diz. Número e palavra: recolhido não pode virar silêncio.
function resumoFechado(estado, temSugestao) {
  if (estado === 'pensando') return 'lendo a conversa'
  if (temSugestao) return 'sugestão pronta'
  return 'nada pendente'
}

// O agente PROPÕE, a dona decide. Ela pede a sugestão apertando "Sugerir";
// ele nunca lê sozinho nem manda sozinho (corte #41 e #45, rodada 5).
//
// O corpo é uma gaveta: abre sozinho só quando há sugestão na mesa ou a
// conversa passou para ela, e fecha de novo quando ela descarta (corte #43:
// Descartar é o único jeito de recolher, não tem toggle separado).
export function PainelAgente({
  modo, estado, sugestao, erro, esperado, passouParaVoce = false,
  bloqueada = false,
  aoConsultar, aoUsar, aoDescartar, aoTrocarModo,
}) {
  const conexao = MODOS[modo]
  const passarParaDona = sugestao?.acao === 'passar_para_dona'
  // Cenário de teste da massa: compara a ação obtida com a esperada.
  const bateu = esperado && sugestao && esperado.acao !== 'nada'
    ? esperado.acao === sugestao.acao
    : null

  const chamaAtencao = Boolean(sugestao) || passouParaVoce
  // Ajuste de estado durante a renderização, não em efeito: sugestão nova ou
  // devolução chegando abre a gaveta, e Descartar fecha de novo. A chave da
  // conversa remonta o painel, então a escolha não vaza para a conversa seguinte.
  const [aberto, setAberto] = useState(chamaAtencao)
  const [chamouAntes, setChamouAntes] = useState(chamaAtencao)
  if (chamaAtencao !== chamouAntes) {
    setChamouAntes(chamaAtencao)
    setAberto(chamaAtencao)
  }

  const pendencias = resumoFechado(estado, Boolean(sugestao))

  const descartar = () => {
    setAberto(false)
    aoDescartar()
  }

  // A sugestão nasce embaixo da conversa, e "Usar no rascunho" ficava escondido
  // atrás do campo de escrever: ao chegar, ela rola para dentro da vista.
  const sugestaoRef = useRef(null)
  useEffect(() => {
    const el = sugestaoRef.current
    if (estado !== 'pronto' || !sugestao || !el) return
    // Só a rolagem da coluna mexe; scrollIntoView rolaria também as molduras
    // de overflow escondido e deslocaria a tela inteira.
    let rolagem = el.parentElement
    while (rolagem && !/(auto|scroll)/.test(getComputedStyle(rolagem).overflowY)) rolagem = rolagem.parentElement
    if (!rolagem) return
    const falta = el.getBoundingClientRect().bottom - rolagem.getBoundingClientRect().bottom
    if (falta > 0) rolagem.scrollTop += falta
  }, [estado, sugestao])

  return (
    <section className={css.painel} aria-label="Agente de atendimento">
      <div className={css.cabeca}>
        <h3><Icone nome="agente" /> Agente da casa</h3>
        {!aberto && (
          <span className={`${css.resumoFechado} ${chamaAtencao ? css.pendente : ''}`}>
            {pendencias}
          </span>
        )}
        <div className={css.direita}>
          <Botao onClick={aoConsultar} disabled={estado === 'pensando'}>
            {estado === 'pensando' ? 'Lendo' : 'Sugerir'}
          </Botao>
        </div>
      </div>

      {aberto && (
        <div className={css.corpo} id="corpo-do-agente">
          {estado === 'ocioso' && (
            <p className={css.tranquilo}>
              <Icone nome="check" /> Nada pendente nesta conversa.
            </p>
          )}

          {estado === 'pensando' && (
            <p className={css.pensando}>
              {modo === 'simulado'
                ? 'Lendo a conversa, a ficha e o cardápio do dia…'
                : `Perguntando ao Claude (${conexao.rotulo.toLowerCase()})…`}
            </p>
          )}

          {estado === 'erro' && (
            <p className={css.erro} role="alert">
              <Icone nome="alerta" /> {erro ?? 'O agente não respondeu.'}
            </p>
          )}

          {estado === 'pronto' && sugestao && (
            <div ref={sugestaoRef} className={passarParaDona ? css.sugestaoDona : css.sugestao}>
              <p className={css.rotuloSugestao}>
                {passarParaDona ? 'Passar para você decidir:' : 'Você pediu:'}
              </p>
              <p>{sugestao.texto}</p>
              {/* Uma pílula por linha. Quem está cozinhando não lê sete de
                  enfiada, e latência, token e dólar não decidem nada para ela:
                  foram para a bancada de teste, no fim do painel. */}
              <p className={css.marcaSugestao}>
                {passarParaDona
                  ? <Pilula tom="aviso">o automático não responde isso</Pilula>
                  : <Pilula tom="info" fina>{sugestao.intencao.rotulo}</Pilula>}
              </p>
              <div className={css.rodapeSugestao}>
                <Botao variante="primario" disabled={bloqueada} onClick={() => aoUsar(sugestao.texto)}>
                  Usar no rascunho
                </Botao>
                <Botao variante="discreto" onClick={descartar}>Descartar</Botao>
              </div>
              {bloqueada && (
                <p className={css.pensando}>
                  Cadastro bloqueado: nada sai desta conversa. Desbloqueie na ficha primeiro.
                </p>
              )}
            </div>
          )}

          {/* Bancada de teste: o que serve a quem constrói o protótipo, e não a
              quem atende. Fechada por padrão, e some da build dela: quem
              atende não escolhe modo de conexão nem lê latência, token ou
              custo em dólar. O time continua vendo em desenvolvimento. */}
          {import.meta.env.DEV && (
            <details className={css.bancada}>
              <summary>Bancada de teste</summary>
              <div className={css.linhaBancada}>
                <label htmlFor="modo-agente">Modo de conexão</label>
                <select
                  id="modo-agente"
                  className={css.seletor}
                  value={modo}
                  onChange={(e) => aoTrocarModo(e.target.value)}
                >
                  {listaDeModos().map((m) => (
                    <option key={m.chave} value={m.chave}>{m.rotulo}</option>
                  ))}
                </select>
                <span className={css.conexao}>{conexao.detalhe}</span>
              </div>

              {esperado && sugestao && (
                <p className={css.cenario}>
                  Cenário de teste: {esperado.cenario}. Esperado: <b>{esperado.acao}</b>, obtido: <b>{sugestao.acao}</b>
                  {bateu === true && <span className={css.bateu}> · bateu</span>}
                  {bateu === false && <span className={css.errou}> · não bateu</span>}
                </p>
              )}

              {sugestao && (
                <>
                  <p className={css.medida}>
                    modelo {sugestao.modelo}
                    {' · '}confiança {Math.round(sugestao.confianca * 100)}%
                    {' · '}{sugestao.latenciaMs} ms
                    {' · '}{sugestao.tokensMedidos != null
                      ? `${sugestao.tokensMedidos} tokens medidos`
                      : `~${sugestao.tokensEstimados} tokens estimados`}
                    {sugestao.custoUsd != null && ` · US$ ${sugestao.custoUsd.toFixed(4)}`}
                    {sugestao.estruturada === false && ' · resposta sem estrutura'}
                  </p>
                  <pre className={css.prompt}>{sugestao.prompt}</pre>
                </>
              )}
            </details>
          )}
        </div>
      )}
    </section>
  )
}
