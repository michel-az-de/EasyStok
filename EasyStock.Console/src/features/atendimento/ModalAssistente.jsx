import { useEffect, useRef, useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { Icone } from '../../componentes/Icone'
import { ModalConfirmar } from '../../componentes/ModalConfirmar'
import { PainelMovel, usePainelMovel } from '../../componentes/PainelMovel'
import { Pilula } from '../../componentes/Pilula'
import { useAcessoModulos } from '../../aplicacao/acessoModulos'
import { useChecagemAssistente } from '../../aplicacao/useChecagemAssistente'
import { usePainelDoAssistenteNaConversa } from '../../aplicacao/painelDoAssistente'
import {
  PERGUNTA_AO_DESLIGAR, contagemDasEtapas, estadoDaEtapa, podeLigarAssistente, vereditoDaChecagem,
} from '../../dominio/checagemAssistente'
import { primeiroNomeDaConversa } from '../../dominio/nomeDoContato'
import css from './atendimento.module.css'

// #2496: por que o Assistente responde ou não esta conversa. As etapas vêm da API, as mesmas do turno do
// robô, todas à vista (o robô para na primeira que barra). A chave liga e desliga o Assistente na loja toda;
// as instruções e as ferramentas são as que o modelo recebe nesta conversa, só para ler.
// #2855: com o painel entregue pelo App, o modal ganha as abas Uso e cota, Fluxo e Histórico ao lado da conversa.
// #3210: o invólucro é um PainelMovel (não modal): arrasta, encaixa e lembra a posição por loja e usuário, e a conversa
// atrás segue clicável. Esc e Fechar devolvem o foco ao campo de mensagem; a confirmação (ModalConfirmar) fica por cima.
export function ModalAssistente({ conversa, aoFechar, escopoMovel = 'assistente:demo:demo' }) {
  const movel = usePainelMovel(escopoMovel, 'assistente')
  const soltar = movel.soltar
  useEffect(() => { soltar() }, [soltar])
  const focarCampo = () => {
    const campo = document.querySelector('[data-area-envio] textarea:not(:disabled)')
    if (!campo) return movel.devolverFoco()
    campo.focus({ preventScroll: true }); return true
  }
  const fechar = () => { focarCampo(); aoFechar() }
  // Esc na fase de bolha: a alça e o redimensionar do painel consomem o Esc que desfaz um movimento (defaultPrevented); sem nada a
  // desfazer ele chega aqui e fecha. Um diálogo aberto por cima (a confirmação) fica com o Esc. Com o foco no corpo da página
  // (clique em texto do painel), o ouvinte do documento fecha.
  const aoTeclar = (e) => {
    if (e.key !== 'Escape' || e.defaultPrevented || e.target.closest?.('dialog')) return
    e.stopPropagation(); fechar()
  }
  const fecharRef = useRef(fechar)
  useEffect(() => { fecharRef.current = fechar })
  useEffect(() => {
    const ouvir = (e) => { if (e.key === 'Escape' && !e.defaultPrevented && e.target === document.body && !document.querySelector('dialog[open]')) fecharRef.current() }
    document.addEventListener('keydown', ouvir)
    return () => document.removeEventListener('keydown', ouvir)
  }, [])
  const { acoes } = useAcessoModulos()
  const { carregando, checagem, erro, salvandoChave, erroChave, checarDeNovo, mudarChave } = useChecagemAssistente(conversa.id)
  const [sinalRecarregar, setSinalRecarregar] = useState(0)
  const veredito = vereditoDaChecagem(checagem)
  const contagem = contagemDasEtapas(checagem?.etapas)
  const podeLigar = podeLigarAssistente(acoes)
  const renderizarPainel = usePainelDoAssistenteNaConversa()

  const [desligando, setDesligando] = useState(false)
  function aoMudarChave(ativo) {
    if (!ativo) { setDesligando(true); return }
    mudarChave(ativo)
  }

  const desenharEtapa = (etapa, semDetalhe = false) => {
    const estado = estadoDaEtapa(etapa)
    return (
      <li key={etapa.codigo} className={css['etapa_' + estado.chave]} data-etapa={etapa.codigo} data-estado={estado.chave}>
        <Pilula tom={estado.tom} icone={estado.icone} tamanho="inline">{estado.rotulo}</Pilula>
        <span className={css.checagemTexto}>
          <strong>{etapa.titulo}</strong>
          {!semDetalhe && <span>{etapa.detalhe}</span>}
        </span>
      </li>
    )
  }
  const etapasQueBarram = (checagem?.etapas ?? []).filter((etapa) => estadoDaEtapa(etapa).chave === 'barra')
  // #2857: com o painel, o motivo do bloqueio (a cota, por exemplo) aparece no veredito e dentro de "Etapas do robô"; a linha da etapa que barra mostra só o selo e o título.
  const detalheDoVeredito = veredito?.detalhe

  const daConversa = (
    <>
        {erro && <p role="alert" className={css.checagemErro}>Não consegui checar agora: {erro}</p>}
        {!checagem && carregando && <p role="status" className={css.checagemNota}>Checando…</p>}
        {checagem && (
          <div className={css.checagem} aria-busy={carregando}>
            <p className={`${css.checagemVeredito} ${css['checagemVeredito_' + veredito.tom]}`} role="status" data-veredito={veredito.tom}>
              <Icone nome={checagem.responde ? 'circle-check' : 'circle-x'} />
              <span className={css.checagemTexto}>
                <strong>{veredito.titulo}</strong>
                {detalheDoVeredito && <span>{detalheDoVeredito}</span>}
              </span>
            </p>

            <div className={css.checagemChave}>
              <input
                id="checagem-chave" type="checkbox" role="switch" checked={checagem.ativo} aria-checked={checagem.ativo}
                disabled={!podeLigar || salvandoChave} aria-describedby="checagem-chave-efeito"
                onChange={(e) => aoMudarChave(e.target.checked)}
              />
              <span className={css.checagemTexto}>
                <label htmlFor="checagem-chave" className={css.checagemRotulo}>
                  Assistente {checagem.ativo ? 'ligado' : 'desligado'} na loja
                </label>
                <span id="checagem-chave-efeito">
                  {checagem.ativo
                    ? 'Vale para a loja toda: o Assistente responde sozinho e chama a equipe quando precisa.'
                    : 'Vale para a loja toda: as conversas vão para a equipe.'}
                  {!podeLigar && ' Só quem administra a loja muda esta chave.'}
                </span>
              </span>
            </div>
            {erroChave && <p role="alert" className={css.checagemErro}>A chave não mudou: {erroChave}</p>}

            {renderizarPainel ? (
              <>
                {etapasQueBarram.length > 0 && <ol className={css.checagemEtapas}>{etapasQueBarram.map((e) => desenharEtapa(e, true))}</ol>}
                <details className={css.checagemDetalhes}>
                  <summary>Etapas do robô ({contagem.passa} passam, {contagem.barra} barram, {contagem.fora} não se aplicam)</summary>
                  <ol className={css.checagemEtapas}>{checagem.etapas.map(desenharEtapa)}</ol>
                </details>
              </>
            ) : (
              <>
                <h3 className={css.checagemTitulo}>
                  Etapas, na ordem do robô
                  <small>{contagem.passa} passam, {contagem.barra} barram, {contagem.fora} não se aplicam</small>
                </h3>
                <ol className={css.checagemEtapas}>{checagem.etapas.map(desenharEtapa)}</ol>
                <details className={css.checagemDetalhes}>
                  <summary>Instruções que o Assistente recebe nesta conversa ({checagem.prompt.length.toLocaleString('pt-BR')} caracteres)</summary>
                  <pre className={css.checagemPrompt} tabIndex={0}>{checagem.prompt}</pre>
                </details>
                <details className={css.checagemDetalhes}>
                  <summary>Ferramentas que ele pode usar ({checagem.ferramentas.length})</summary>
                  <ul className={css.checagemFerramentas}>
                    {checagem.ferramentas.map((f) => (
                      <li key={f.nome} data-ligada={f.ligada !== false}>
                        <code>{f.nome}</code> {f.ligada === false ? <strong>Desligada pela loja.</strong> : null} {f.descricao}
                      </li>
                    ))}
                  </ul>
                </details>
              </>
            )}
            {!renderizarPainel && <p className={css.checagemNota}>Tom, saudações, cota, roteiro e ferramentas do Assistente ficam em Gestão › Atendimento.</p>}
          </div>
        )}
    </>
  )

  return (
    <PainelMovel
      controle={{ ...movel, devolverFoco: focarCampo }} titulo="Assistente nesta conversa" alinharDireita
      larguraInicial={renderizarPainel ? 560 : 440} alturaInicial={560} aoRecolher={aoFechar}
      atributos={{ role: 'dialog', onKeyDown: aoTeclar }}
    >
      <p className={css.checagemNota}>{`O que decide se o Assistente responde ${primeiroNomeDaConversa(conversa)}.`}</p>
      {renderizarPainel
        ? renderizarPainel({ conversaId: conversa.id, podeEditar: acoes?.editarAtendimento === true, sinalRecarregar, abaAntes: { id: 'conversa', rotulo: 'Esta conversa', conteudo: daConversa } })
        : daConversa}
      <div className={css.assistenteRodape}>
        <Botao icone="refresh-cw" onClick={() => { checarDeNovo(); setSinalRecarregar((n) => n + 1) }} disabled={carregando}>Checar de novo</Botao>
        <Botao variante="primario" onClick={fechar}>Fechar</Botao>
      </div>
      {desligando && (
        <ModalConfirmar titulo="Desligar o Assistente?" descricao={PERGUNTA_AO_DESLIGAR} rotuloConfirmar="Desligar o Assistente"
          aoConfirmar={() => mudarChave(false)} aoFechar={() => setDesligando(false)} />
      )}
    </PainelMovel>
  )
}
