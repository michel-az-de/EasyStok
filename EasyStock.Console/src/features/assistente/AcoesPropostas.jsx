import { useId, useLayoutEffect, useRef } from 'react'
import { Botao } from '../../componentes/Botao'
import { Icone } from '../../componentes/Icone'
import { ACOES_DO_ASSISTENTE, descreverAcaoDoAssistente } from '../../dominio/acoesDoAssistente'
import { focarResultadoDaAcao } from './focoDaAcao'
import css from './assistente.module.css'

const ICONE = {
  [ACOES_DO_ASSISTENTE.RETOMAR_AUTOMATICO]: 'agente',
  [ACOES_DO_ASSISTENTE.ENVIAR_CARDAPIO]: 'cardapio',
  [ACOES_DO_ASSISTENTE.NOTA_INTERNA]: 'nota',
  [ACOES_DO_ASSISTENTE.RASCUNHO]: 'modelo',
  [ACOES_DO_ASSISTENTE.ABRIR_TELA]: 'ficha',
}

// #1445: o que o assistente propôs fazer, uma linha por ação, com um botão só. Nada roda
// antes do clique; o único botão que manda algo ao cliente é o primário ("Enviar ao cliente").
// Ignorar a linha é descartar: sem botão a mais.
export function AcoesPropostas({ acoes, nomeCliente, aoExecutar, motivoDeEnvio, semCadastro }) {
  const prefixoId = useId()
  const origensRef = useRef(new Map())
  useLayoutEffect(() => {
    for (const acao of acoes ?? []) {
      if (acao.estado === 'feita' || acao.estado === 'falhou') {
        focarResultadoDaAcao(origensRef.current.get(acao.chave), document, acao.estado)
        origensRef.current.delete(acao.chave)
      }
    }
  }, [acoes])
  if (!acoes?.length) return null
  return (
    <ul className={css.acoesPropostas} aria-label="Ações sugeridas pelo assistente">
      {acoes.map((acao, indice) => {
        const rotulo = descreverAcaoDoAssistente(acao, nomeCliente)
        if (!rotulo) return null
        const impedimento = (rotulo.saiParaCliente && motivoDeEnvio)
          || (acao.tipo === ACOES_DO_ASSISTENTE.NOTA_INTERNA && semCadastro && 'Salve o cadastro do cliente na Ficha antes de anotar.')
        const erroId = `${prefixoId}-${indice}-erro`
        return (
          <li key={acao.chave} className={css.acaoProposta}>
            <div className={css.acaoTexto}>
              <span className={css.acaoTitulo}><Icone nome={ICONE[acao.tipo]} tamanho={16} /> {rotulo.titulo}</span>
              {rotulo.detalhe && <span className={css.acaoDetalhe}>{rotulo.detalhe}</span>}
              {acao.estado === 'falhou' && (
                <span className={css.acaoErro} id={erroId} role="alert"><Icone nome="alerta" tamanho={14} /> {acao.erro}</span>
              )}
              {impedimento && acao.estado !== 'feita' && <span className={css.acaoDetalhe} id={`${erroId}-impedimento`}>{impedimento}</span>}
            </div>
            {acao.estado === 'feita' ? (
              <output className={css.acaoFeita} tabIndex={-1} data-status-acao><Icone nome="check" tamanho={16} /> {rotulo.feito}</output>
            ) : (
              <Botao
                variante={rotulo.saiParaCliente ? 'primario' : 'secundario'}
                className={css.acaoBotao}
                disabled={acao.estado === 'executando' || Boolean(impedimento)}
                aria-describedby={impedimento ? `${erroId}-impedimento` : acao.estado === 'falhou' ? erroId : undefined}
                onClick={(evento) => {
                  origensRef.current.set(acao.chave, { botao: evento.currentTarget, item: evento.currentTarget.closest('li') })
                  aoExecutar(acao)
                }}
              >
                {acao.estado === 'executando' ? 'Confirmando…' : acao.estado === 'falhou' ? 'Tentar novamente' : rotulo.botao}
              </Botao>
            )}
          </li>
        )
      })}
    </ul>
  )
}
