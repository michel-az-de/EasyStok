import { useEffect, useRef } from 'react'
import { Icone } from '../../componentes/Icone'
import { Pilula } from '../../componentes/Pilula'
import { rotuloDoDia } from '../../dominio/formato'
import { intercalarNotas } from '../../dominio/notas'
import { Balao } from './Balao'
import css from './atendimento.module.css'

// Divisória entre atendimentos (decisão 46, "Encerrar" v2): encerrado nunca
// reabre o MESMO atendimento — a próxima mensagem do cliente começa um
// atendimento novo na mesma conversa, e o fio marca essa fronteira em vez de
// deixar a passagem muda entre duas mensagens quaisquer.
function Divisoria({ numero }) {
  return (
    <div className={css.divisoriaAtendimento} role="separator">
      <span>Atendimento {numero} encerrado · novo atendimento a seguir</span>
    </div>
  )
}

// Separador de dia (achado 8, pendência 18 da banca 64): pílula "Hoje",
// "Ontem" ou a data, antes da primeira mensagem de cada dia. Sem isto, duas
// mensagens de dias diferentes (Sandra: 21:45 de um dia, 10:05 do seguinte)
// ficavam uma embaixo da outra sem nenhuma marca de que o dia mudou.
function SeparadorDeDia({ rotulo }) {
  return (
    <div className={css.separadorDia} role="separator">
      <Pilula tom="neutro">{rotulo}</Pilula>
    </div>
  )
}

// Rodada 11 (issue #8, registro 92): a casa "digitando" antes da resposta
// automática sair, do lado de quem responde, com o mesmo selo "automática"
// da bolha que vem a seguir. Sem isto a resposta caía pronta junto da fala do
// cliente e ninguém via o automático trabalhar. Os três pontos animam; com
// movimento reduzido (base.css) ficam parados e o texto continua dizendo.
function Digitando() {
  return (
    <div className={`${css.balao} ${css.saida} ${css.doAutomatico} ${css.digitando}`}>
      <span className={css.pontos} aria-hidden="true"><i /><i /><i /></span>
      <span className={css.meta}>
        <span className={css.selo}><Icone nome="raio" /> automática digitando</span>
      </span>
    </div>
  )
}

// Nota interna como post-it (#1441): entra no fio pela hora em que foi escrita, do
// lado de quem atende, e nunca vai ao cliente. A nota é do cadastro, então uma nota
// de outra conversa do mesmo cliente também aparece aqui, na hora dela.
function PostIt({ nota }) {
  return (
    <aside className={css.postIt} aria-label="Nota interna">
      <span className={css.postItQuem}><Icone nome="nota" /> Nota interna · {nota.autor} · {nota.em}</span>
      <p>{nota.texto}</p>
    </aside>
  )
}

const diaDe = (iso) => {
  const d = new Date(iso)
  return d.getFullYear() * 10000 + d.getMonth() * 100 + d.getDate()
}

export function Thread({
  mensagens, chaveRolagem, fronteiras = [], agora, aoAbrirDefinicaoAutomatica, aoReenviar, digitando = false, notas = [],
}) {
  const fim = useRef(null)

  useEffect(() => {
    fim.current?.scrollIntoView({ block: 'end' })
  }, [mensagens.length, chaveRolagem, digitando, notas.length])

  // Intercala as divisórias na posição certa: por ÍNDICE (`aposIndice`), não
  // por horário — o relógio simulado anda em passos e duas mensagens de
  // atendimentos diferentes podem nascer no mesmo instante gravado.
  // `trocaDeVoz` continua olhando `mensagens` puro (índice `i`), a divisória
  // não é remetente e não deve contar como troca de voz.
  let proxima = 0
  const linhas = []
  let i = -1
  for (const item of intercalarNotas(mensagens, notas)) {
    if (item.tipo === 'nota') {
      linhas.push({ tipo: 'nota', chave: `nota-${item.nota.id}`, nota: item.nota })
      continue
    }
    i += 1
    const m = item.mensagem
    while (proxima < fronteiras.length && i > fronteiras[proxima].aposIndice) {
      linhas.push({ tipo: 'divisoria', chave: `divisoria-${fronteiras[proxima].numero}`, numero: fronteiras[proxima].numero })
      proxima += 1
    }
    if (i === 0 || diaDe(m.em) !== diaDe(mensagens[i - 1].em)) {
      linhas.push({ tipo: 'dia', chave: `dia-${m.id}`, rotulo: rotuloDoDia(m.em, agora) })
    }
    linhas.push({
      tipo: 'mensagem', chave: m.id, mensagem: m, trocaDeVoz: i > 0 && mensagens[i - 1].dir !== m.dir,
    })
  }

  return (
    <div className={css.thread} role="log" aria-live="polite" aria-label="Mensagens da conversa">
      {linhas.map((l) => {
        if (l.tipo === 'divisoria') return <Divisoria key={l.chave} numero={l.numero} />
        if (l.tipo === 'dia') return <SeparadorDeDia key={l.chave} rotulo={l.rotulo} />
        if (l.tipo === 'nota') return <PostIt key={l.chave} nota={l.nota} />
        return (
          <Balao
            key={l.chave}
            mensagem={l.mensagem}
            trocaDeVoz={l.trocaDeVoz}
            aoAbrirDefinicaoAutomatica={aoAbrirDefinicaoAutomatica}
            aoReenviar={aoReenviar}
          />
        )
      })}
      {digitando && <Digitando />}
      <div ref={fim} />
    </div>
  )
}
