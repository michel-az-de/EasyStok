import { useEffect, useRef, useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { Icone } from '../../componentes/Icone'
import { Selo } from '../../componentes/Selo'
import {
  ETAPAS_CORRIDA, cancelamentoDaCorrida, gorjetasDaCorrida, iniciaisDoMotorista, linkDeRastreio, statusDaCorrida,
} from '../../dominio/entregasApi'
import { horaCurta, moedaDosCentavos, telefoneDoE164 } from '../../dominio/formato'
import css from './corridas.module.css'

const SEM_CONFIRMACAO = { rotulo: 'Sem confirmação da Lalamove', tom: 'alerta', icone: 'alerta', etapa: null }

// A corrida chamada pela API (#2162, redesenho #2168): Selo do status, linha do tempo das quatro etapas,
// motorista com placa e telefone, e só o que a Lalamove ainda aceita. Cancelar, dar gorjeta e liberar a
// viagem custam ou desfazem: pedem confirmação em linha. Encerrada, vira uma linha só.
export function CorridaLalamove({ corrida, travado, atualizadaEm, semResposta, cancelar, gorjeta, liberar, atualizarAgora }) {
  const status = corrida.conferirNoProvedor ? SEM_CONFIRMACAO : statusDaCorrida(corrida)
  const [confirmando, setConfirmando] = useState(null) // { tipo: 'cancelar' | 'liberar' } | { tipo: 'gorjeta', centavos }
  const confirmar = (acao) => acao().then((ok) => { if (ok) setConfirmando(null) })
  // A confirmação aparece no lugar do botão que a abriu: o foco vai para o botão que confirma.
  const confirmarRef = useRef(null)
  useEffect(() => { if (confirmando) confirmarRef.current?.focus() }, [confirmando])
  // A corrida andou (achou motorista, encerrou): a pergunta aberta não vale mais.
  const [statusDaPergunta, setStatusDaPergunta] = useState(corrida.status)
  if (statusDaPergunta !== corrida.status) {
    setStatusDaPergunta(corrida.status)
    setConfirmando(null)
  }
  const veiculo = corrida.veiculo === 'Carro' ? 'carro' : 'moto'

  if (!corrida.ativa) {
    return (
      <li className={`${css.corrida} ${css.corridaEncerrada}`} aria-label={`Corrida Lalamove: ${status.rotulo}`}>
        <div className={css.linha}>
          <Selo tom={status.tom} icone={status.icone}>{status.rotulo}</Selo>
          <span className={`${css.apoio} ${css.cresce}`}>
            Lalamove de {veiculo}{corrida.encerradaEm ? ` · ${horaCurta(corrida.encerradaEm)}` : ''}
          </span>
          <p className={css.custo}>{moedaDosCentavos(corrida.custoCentavos)}</p>
        </div>
      </li>
    )
  }

  const cancelamento = cancelamentoDaCorrida(corrida)
  const procurando = corrida.status === 'ProcurandoMotorista'
  const link = linkDeRastreio(corrida)
  return (
    <li className={css.corrida} aria-label={`Corrida Lalamove: ${status.rotulo}`}>
      <div className={css.linha}>
        <span aria-live="polite" className={css.cresce}><Selo tom={status.tom} icone={status.icone}>{status.rotulo}</Selo></span>
        <p className={css.custo}>
          {moedaDosCentavos(corrida.custoCentavos)}
          {/* Diferença sobre a cotação: procurando motorista é a gorjeta; depois pode incluir ajuste da Lalamove. */}
          <small>{corrida.gorjetaCentavos > 0 ? `${moedaDosCentavos(corrida.gorjetaCentavos)} acima da cotação` : `Lalamove de ${veiculo}`}</small>
        </p>
      </div>

      {corrida.conferirNoProvedor ? (
        <ConferirNoPainel travado={travado} confirmando={confirmando?.tipo === 'liberar'} confirmarRef={confirmarRef}
          aoPedir={() => setConfirmando({ tipo: 'liberar' })} aoDesistir={() => setConfirmando(null)}
          aoLiberar={() => confirmar(() => liberar(corrida.id))} />
      ) : (
        <LinhaDoTempo etapa={status.etapa} />
      )}

      {corrida.motorista && (
        <div className={css.motorista}>
          <span className={css.avatar} aria-hidden="true">{iniciaisDoMotorista(corrida.motorista)}</span>
          <div className={css.quem}>
            <strong>{corrida.motorista}</strong>
            {corrida.motoristaPlaca && <span className={css.placa} aria-label={`Placa ${corrida.motoristaPlaca}`}>{corrida.motoristaPlaca}</span>}
          </div>
          <div className={css.contatos}>
            {corrida.motoristaTelefone && (
              <a className={css.linkAcao} href={`tel:${corrida.motoristaTelefone.replace(/[^\d+]/g, '')}`}
                aria-label={`Ligar para ${corrida.motorista}, ${telefoneDoE164(corrida.motoristaTelefone)}`}>
                <Icone nome="phone" tamanho={20} />{telefoneDoE164(corrida.motoristaTelefone) || corrida.motoristaTelefone}
              </a>
            )}
            {link && (
              <a className={css.linkAcao} href={link} target="_blank" rel="noopener noreferrer"
                aria-label="Acompanhar no mapa da Lalamove (abre em nova aba)">
                <Icone nome="external-link" tamanho={20} />Acompanhar no mapa
              </a>
            )}
          </div>
        </div>
      )}
      {!corrida.motorista && link && (
        <a className={css.linkAcao} href={link} target="_blank" rel="noopener noreferrer"
          aria-label="Acompanhar no mapa da Lalamove (abre em nova aba)">
          <Icone nome="external-link" tamanho={20} />Acompanhar no mapa
        </a>
      )}

      {(procurando || cancelamento) && (
        <div className={css.acoes}>
          {procurando && (
            confirmando?.tipo === 'gorjeta' ? (
              <div className={css.confirmacao} role="alertdialog" aria-label="Confirmar a gorjeta">
                <p>
                  Dar {moedaDosCentavos(confirmando.centavos)} de gorjeta? É o total, não um acréscimo: entra no preço da corrida
                  para a Lalamove achar motorista mais rápido.
                </p>
                <div className={css.linha}>
                  <Botao variante="primario" disabled={travado} ref={confirmarRef} onClick={() => confirmar(() => gorjeta(corrida.id, confirmando.centavos))}>
                    Dar {moedaDosCentavos(confirmando.centavos)} de gorjeta
                  </Botao>
                  <Botao variante="texto" disabled={travado} onClick={() => setConfirmando(null)}>Cancelar</Botao>
                </div>
              </div>
            ) : (
              <div className={css.gorjetas}>
                <span className={css.rotulo}>Gorjeta para achar motorista</span>
                {gorjetasDaCorrida(corrida).map(({ centavos, disponivel }) => (
                  <Botao key={centavos} variante="secundario" disabled={travado || !disponivel}
                    aria-label={`Gorjeta total de ${moedaDosCentavos(centavos)}`}
                    onClick={() => setConfirmando({ tipo: 'gorjeta', centavos })}>{moedaDosCentavos(centavos)}</Botao>
                ))}
              </div>
            )
          )}
          {cancelamento?.pode && (
            confirmando?.tipo === 'cancelar' ? (
              <div className={`${css.confirmacao} ${css.confirmacaoPerigo}`} role="alertdialog" aria-label="Confirmar o cancelamento">
                <p>Cancelar a corrida? A Lalamove avisa o motorista e a viagem volta a montar.</p>
                <div className={css.linha}>
                  <Botao variante="perigo" disabled={travado} ref={confirmarRef} onClick={() => confirmar(() => cancelar(corrida.id))}>Cancelar corrida</Botao>
                  <Botao variante="texto" disabled={travado} onClick={() => setConfirmando(null)}>Manter corrida</Botao>
                </div>
              </div>
            ) : (
              <div className={css.linha}>
                <Botao variante="texto" icone="circle-x" disabled={travado} onClick={() => setConfirmando({ tipo: 'cancelar' })}>Cancelar corrida</Botao>
                {cancelamento.texto && <span className={css.apoio}>{cancelamento.texto}</span>}
              </div>
            )
          )}
          {cancelamento && !cancelamento.pode && <p className={css.apoio}>{cancelamento.texto}</p>}
        </div>
      )}

      <div className={css.rodape}>
        <span className={css.apoio}>
          {semResposta ? 'A Lalamove não respondeu na última consulta. ' : ''}
          {atualizadaEm ? `Atualizado às ${horaCurta(atualizadaEm)}` : ''}
        </span>
        <Botao variante="texto" icone="refresh-cw" disabled={travado} onClick={atualizarAgora}>Atualizar agora</Botao>
      </div>
    </li>
  )
}

function LinhaDoTempo({ etapa }) {
  if (etapa == null) return null
  return (
    <ol className={css.etapas} aria-label="Etapas da corrida">
      {ETAPAS_CORRIDA.map((rotulo, i) => {
        const numero = i + 1
        const classe = numero < etapa || etapa === ETAPAS_CORRIDA.length ? css.etapaFeita : numero === etapa ? css.etapaAtual : ''
        return (
          <li key={rotulo} className={`${css.etapa} ${classe}`} aria-current={numero === etapa ? 'step' : undefined}>{rotulo}</li>
        )
      })}
    </ol>
  )
}

// #2168: a Lalamove não confirmou e não há número do pedido para perguntar. A viagem fica presa até a atendente
// olhar o painel da Lalamove: se a corrida não está lá, ela libera a viagem para chamar de novo.
function ConferirNoPainel({ travado, confirmando, confirmarRef, aoPedir, aoDesistir, aoLiberar }) {
  return (
    <output className={css.aviso}>
      <strong>A Lalamove não confirmou esta corrida.</strong>
      <span>
        Antes de chamar outra, confira no painel ou no app da Lalamove se ela foi criada. Se estiver lá, o EasyStok não
        consegue acompanhá-la: cancele por lá antes de liberar, ou siga a entrega pelo app da Lalamove.
      </span>
      {confirmando ? (
        <div className={css.confirmacao} role="alertdialog" aria-label="Confirmar a liberação da viagem">
          <p>Conferiu e a corrida não está na Lalamove? A viagem volta a montar e dá para chamar de novo.</p>
          <div className={css.linha}>
            <Botao variante="primario" disabled={travado} ref={confirmarRef} onClick={aoLiberar}>Não está lá, liberar a viagem</Botao>
            <Botao variante="texto" disabled={travado} onClick={aoDesistir}>Cancelar</Botao>
          </div>
        </div>
      ) : (
        <div className={css.linha}><Botao variante="secundario" disabled={travado} onClick={aoPedir}>Conferi no painel</Botao></div>
      )}
    </output>
  )
}
