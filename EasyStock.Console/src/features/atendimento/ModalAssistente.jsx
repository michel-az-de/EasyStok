import { Botao } from '../../componentes/Botao'
import { Icone } from '../../componentes/Icone'
import { Modal } from '../../componentes/Modal'
import { Pilula } from '../../componentes/Pilula'
import { useAcessoModulos } from '../../aplicacao/acessoModulos'
import { useChecagemAssistente } from '../../aplicacao/useChecagemAssistente'
import {
  PERGUNTA_AO_DESLIGAR, contagemDasEtapas, estadoDaEtapa, podeLigarAssistente, vereditoDaChecagem,
} from '../../dominio/checagemAssistente'
import { primeiroNomeDaConversa } from '../../dominio/nomeDoContato'
import css from './atendimento.module.css'

// #2496: por que o Assistente responde ou não esta conversa. As etapas vêm da API, as mesmas do turno do
// robô, todas à vista (o robô para na primeira que barra). A chave liga e desliga o Assistente na loja toda;
// as instruções e as ferramentas são as que o modelo recebe nesta conversa, só para ler.
export function ModalAssistente({ conversa, aoFechar }) {
  const { acoes } = useAcessoModulos()
  const { carregando, checagem, erro, salvandoChave, erroChave, checarDeNovo, mudarChave } = useChecagemAssistente(conversa.id)
  const veredito = vereditoDaChecagem(checagem)
  const contagem = contagemDasEtapas(checagem?.etapas)
  const podeLigar = podeLigarAssistente(acoes)

  function aoMudarChave(ativo) {
    if (!ativo && !window.confirm(PERGUNTA_AO_DESLIGAR)) return
    mudarChave(ativo)
  }

  return (
    <Modal
      titulo="Assistente nesta conversa"
      descricao={`O que decide se o Assistente responde ${primeiroNomeDaConversa(conversa)}.`}
      largura="md"
      aoFechar={aoFechar}
      rodape={(
        <>
          <Botao icone="refresh-cw" onClick={checarDeNovo} disabled={carregando}>Checar de novo</Botao>
          <Botao variante="primario" onClick={aoFechar}>Fechar</Botao>
        </>
      )}
    >
      {erro && <p role="alert" className={css.checagemErro}>Não consegui checar agora: {erro}</p>}
      {!checagem && carregando && <p role="status" className={css.checagemNota}>Checando…</p>}
      {checagem && (
        <div className={css.checagem} aria-busy={carregando}>
          <p className={`${css.checagemVeredito} ${css['checagemVeredito_' + veredito.tom]}`} role="status" data-veredito={veredito.tom}>
            <Icone nome={checagem.responde ? 'circle-check' : 'circle-x'} />
            <span className={css.checagemTexto}>
              <strong>{veredito.titulo}</strong>
              {veredito.detalhe && <span>{veredito.detalhe}</span>}
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

          <h3 className={css.checagemTitulo}>
            Etapas, na ordem do robô
            <small>{contagem.passa} passam, {contagem.barra} barram, {contagem.fora} não se aplicam</small>
          </h3>
          <ol className={css.checagemEtapas}>
            {checagem.etapas.map((etapa) => {
              const estado = estadoDaEtapa(etapa)
              return (
                <li key={etapa.codigo} className={css['etapa_' + estado.chave]} data-etapa={etapa.codigo} data-estado={estado.chave}>
                  <Pilula tom={estado.tom} icone={estado.icone} tamanho="inline">{estado.rotulo}</Pilula>
                  <span className={css.checagemTexto}>
                    <strong>{etapa.titulo}</strong>
                    <span>{etapa.detalhe}</span>
                  </span>
                </li>
              )
            })}
          </ol>

          <details className={css.checagemDetalhes}>
            <summary>Instruções que o Assistente recebe nesta conversa ({checagem.prompt.length.toLocaleString('pt-BR')} caracteres)</summary>
            <pre className={css.checagemPrompt} tabIndex={0}>{checagem.prompt}</pre>
          </details>
          <details className={css.checagemDetalhes}>
            <summary>Ferramentas que ele pode usar ({checagem.ferramentas.length})</summary>
            <ul className={css.checagemFerramentas}>
              {checagem.ferramentas.map((f) => <li key={f.nome}><code>{f.nome}</code> {f.descricao}</li>)}
            </ul>
          </details>
          <p className={css.checagemNota}>Tom, saudações e mensagens do Assistente ficam em Gestão › Atendimento.</p>
        </div>
      )}
    </Modal>
  )
}
