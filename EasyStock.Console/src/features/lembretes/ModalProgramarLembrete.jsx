import { useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { CampoArea, CampoTexto } from '../../componentes/Campo'
import { Modal } from '../../componentes/Modal'
import {
  OPCAO_OUTRO_HORARIO, OPCOES_QUANDO, quandoDaOpcao, quandoDoHorario,
  rotuloDaOpcao, rotuloDoHorario, sugestoesParaDona,
} from '../../dominio/lembrete'
import css from './lembretes.module.css'

// Programar lembrete (seção 8). Lembrete é dela: nenhum campo, exemplo ou
// erro aqui sugere que o cliente recebe alguma coisa.
export function ModalProgramarLembrete({ conversa, faixa, agora, aoProgramar, aoFechar }) {
  const [texto, setTexto] = useState('')
  const [opcaoId, setOpcaoId] = useState(OPCOES_QUANDO[0].id)
  const [horarioManual, setHorarioManual] = useState('')
  const [vinculada, setVinculada] = useState(Boolean(conversa))
  const [erro, setErro] = useState(null)

  const sugestoes = sugestoesParaDona(conversa, faixa)
  const usaOutro = opcaoId === OPCAO_OUTRO_HORARIO
  const quando = usaOutro
    ? (horarioManual ? quandoDoHorario(horarioManual, agora) : null)
    : quandoDaOpcao(opcaoId, agora)

  const programar = () => {
    const limpo = texto.trim()
    if (!limpo) { setErro('Escreva o lembrete.'); return }
    if (quando == null) { setErro('Escolha um horário.'); return }
    aoProgramar({ texto: limpo, quando, conversaId: vinculada && conversa ? conversa.id : null })
  }

  const aoTeclarNoTexto = (evento) => {
    if (evento.key === 'Enter' && (evento.ctrlKey || evento.metaKey)) {
      evento.preventDefault()
      programar()
    }
  }

  return (
    <Modal
      titulo="Programar lembrete"
      aoFechar={aoFechar}
      rodape={(
        <>
          <Botao variante="texto" onClick={aoFechar}>Cancelar</Botao>
          <Botao variante="primario" icone="bell-plus" onClick={programar}>Programar</Botao>
        </>
      )}
    >
      <div className={css.formulario}>
        <CampoArea
          rotulo="O que lembrar"
          value={texto}
          onChange={(e) => { setTexto(e.target.value); setErro(null) }}
          onKeyDown={aoTeclarNoTexto}
          placeholder="Ex.: ligar para o José"
          rows={2}
          autoFocus
        />

        {erro && <p className={css.erro} role="alert">{erro}</p>}

        {sugestoes.length > 0 && (
          <div className={css.sugestoes}>
            {sugestoes.map((s) => (
              <Botao
                key={s}
                variante="secundario"
                className={css.pilula}
                onClick={() => { setTexto(s); setErro(null) }}
              >
                {s}
              </Botao>
            ))}
          </div>
        )}

        <fieldset className={css.grupoPilulas}>
          <legend className={css.rotuloCampo}>Quando</legend>
          <div className={css.pilulas}>
            {OPCOES_QUANDO.map((opcao) => (
              <Botao
                key={opcao.id}
                variante={opcaoId === opcao.id ? 'primario' : 'secundario'}
                className={css.pilula}
                aria-pressed={opcaoId === opcao.id}
                onClick={() => setOpcaoId(opcao.id)}
              >
                {rotuloDaOpcao(opcao.id, agora)}
              </Botao>
            ))}
            <Botao
              variante={usaOutro ? 'primario' : 'secundario'}
              className={css.pilula}
              aria-pressed={usaOutro}
              onClick={() => setOpcaoId(OPCAO_OUTRO_HORARIO)}
            >
              {usaOutro && horarioManual ? rotuloDoHorario(horarioManual, agora) : 'Outro horário'}
            </Botao>
          </div>
          {usaOutro && (
            <div className={css.horarioManual}>
              <CampoTexto
                rotulo="Horário"
                rotuloOculto
                tipo="time"
                value={horarioManual}
                onChange={(e) => setHorarioManual(e.target.value)}
              />
            </div>
          )}
        </fieldset>

        {conversa && vinculada && (
          <fieldset className={css.grupoPilulas}>
            <legend className={css.rotuloCampo}>Conversa</legend>
            <div className={css.pilulas}>
              <Botao variante="secundario" className={css.pilula} onClick={() => setVinculada(false)}>
                {conversa.nome} ×
              </Botao>
            </div>
          </fieldset>
        )}
      </div>
    </Modal>
  )
}
