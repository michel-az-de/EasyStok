// Controles da frente Anexos que moram dentro do Composer (rodada 7, pedido
// do dono 24/09/2026 04h12). Vive em `features/atendimento`, não em
// `features/anexos`: precisa ficar no MESMO galho de DOM que `.composer`
// (âncora do Popover, `position: sticky`) e feature nenhuma importa outra
// feature (ferramentas/verificar-camadas.mjs) — mesmo motivo já documentado
// no Composer.jsx para o picker de "Prato".
import { Botao } from '../../componentes/Botao'
import { Icone } from '../../componentes/Icone'
import {
  amplitudeSimulada, duracaoExcedida, formatarDuracao, formatarTamanho,
} from '../../dominio/anexos'
import css from './composerAnexos.module.css'

// <label> em vez de <button onClick={() => ref.click()}>: um <input
// type=file> não entra no seletor da varredura de clique nem precisa de ref,
// e a classe global ".sr" (base.css) mantém o campo focável por teclado
// mesmo escondido visualmente (Tab alcança, Enter/Espaço abre o seletor do
// sistema), igual a um botão de verdade.
// `rotulo`/`classeRotulo` (rodada 12, issue #16): o nome visível ao lado do
// clipe. Sem `classeRotulo`, o nome continua só para leitor de tela.
export function BotaoAnexarArquivo({
  disabled, titulo, rotulo = 'Anexar', classeRotulo = 'sr', aoEscolher,
}) {
  return (
    <label className={`${css.botaoAnexo} ${disabled ? css.desabilitado : ''}`} title={titulo}>
      <Icone nome="anexo" /> <span className={classeRotulo}>{rotulo}</span>
      <input
        type="file"
        className="sr"
        disabled={disabled}
        accept="image/jpeg,image/png,image/webp,image/gif,application/pdf"
        onChange={(evento) => {
          const arquivo = evento.target.files?.[0]
          evento.target.value = '' // permite escolher o mesmo arquivo de novo em seguida
          if (arquivo) aoEscolher(arquivo)
        }}
      />
    </label>
  )
}

// Prévia antes de enviar (pesquisa seção B: "card com miniatura, nome do
// arquivo, badge de tipo, botão de remover"). `anexo`: { tipo: 'imagem'|'pdf',
// nome, tamanho, dataUrl }.
export function PreviaAnexo({ anexo, aoRemover }) {
  return (
    <div className={css.previa}>
      {anexo.tipo === 'imagem'
        ? <img className={css.previaMiniatura} src={anexo.dataUrl} alt="" />
        : <span className={css.previaIcone} aria-hidden="true"><Icone nome="nota" /></span>}
      <span className={css.previaTexto}>
        <strong>{anexo.nome}</strong>
        <span>{anexo.tipo === 'pdf' ? 'PDF · ' : ''}{formatarTamanho(anexo.tamanho)}</span>
      </span>
      <button
        type="button"
        className={css.previaRemover}
        onClick={aoRemover}
        aria-label={`Remover anexo ${anexo.nome}`}
      >
        <Icone nome="fechar" />
      </button>
    </div>
  )
}

// Gravador (pedido do dono: "gravar com o microfone... cancelar ou enviar").
// `onda` é decorativa (amplitude simulada, seção B da pesquisa: "pode ser
// simulada, não precisa FFT real"); o cronômetro é de verdade
// (`gravador.duracaoMs`, hooks/useGravadorAudio.js).
export function GravadorAudio({ gravador, aoCancelar, aoEnviar }) {
  if (gravador.estado === 'pedindo') {
    return <p className={css.aviso}>Pedindo acesso ao microfone…</p>
  }
  if (gravador.estado === 'erro') {
    return (
      <p className={css.erro} role="alert">
        <Icone nome="alerta" /> {gravador.erro}
        <button type="button" onClick={gravador.limparErro}>Fechar</button>
      </p>
    )
  }
  const passo = Math.floor(gravador.duracaoMs / 120)
  return (
    <div className={css.gravando}>
      <Icone nome="mic" className={css.gravandoIcone} />
      <span className={css.onda} aria-hidden="true">
        {Array.from({ length: 20 }, (_, indice) => (
          <span key={indice} style={{ height: `${Math.round(amplitudeSimulada(indice + passo) * 100)}%` }} />
        ))}
      </span>
      <span className={css.cronometro}>{formatarDuracao(gravador.duracaoMs)}</span>
      {duracaoExcedida(gravador.duracaoMs) && <span className={css.avisoLimite}>Áudio longo, considere enviar</span>}
      <Botao onClick={aoCancelar}>Cancelar</Botao>
      <Botao variante="primario" onClick={aoEnviar}>
        <Icone nome="enviar" /> Enviar áudio
      </Botao>
    </div>
  )
}
