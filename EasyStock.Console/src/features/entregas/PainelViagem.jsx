import { useEffect, useState } from 'react'
import { useDroppable } from '@dnd-kit/core'
import { Anel } from '../../componentes/Anel'
import { Botao } from '../../componentes/Botao'
import { EscolhaDeProvedorEntrega } from '../../componentes/EscolhaDeProvedorEntrega'
import { Icone } from '../../componentes/Icone'
import { Pilula } from '../../componentes/Pilula'
import { indiceDoPasso } from '../../dominio/esteira'
import { horaCurta, moeda, partesDoEndereco, plural } from '../../dominio/formato'
import { marcosDaViagem } from '../../dominio/viagem'
import { textoDoEntregador } from '../../dominio/despacho'
import { AVISO_SIMULADO, provedoresParaDespacho } from '../../dominio/integracoes'
import { PASSOS_CORRIDA, corridaEhFinal, ehProvedorIntegrado, resumoDaCorrida } from '../../dominio/corrida'
import css from './dashboard.module.css'

// Simulação do chamado (estudo 18 § 2.6, "Chamar entregador"): procurando
// por 3 s reais, depois achou. Tique de tela, não do relógio da aplicação —
// esperar o relógio da simulação levaria minutos reais de espera, e aqui é
// só o toque de "achou motoboy", não a rota em si. PONTO ONDE A API REAL
// ENTRA (estudo 18, E1): trocar estes dois `setTimeout` por
// `infra/provedoresDeEntrega.js: cotar(viagem)` + `chamar(viagem)`, e os
// webhooks (`ORDER_STATUS_CHANGED`/`DRIVER_ASSIGNED`) tomam o lugar do
// segundo tique.
//
// Rodada 12 (issue #17): o achado traz o registro inteiro (nome, veículo,
// placa, empresa), no formato que a Lalamove devolveria. Inventado, e a tela
// marca "Simulado" ao lado.
const ACHADO_SIMULADO = { nome: 'Carlos Mendes', veiculo: 'moto', placa: 'FJK2B41', empresa: 'lalamove' }

function useSimulacaoDoChamado(viagem, atualizarChamado) {
  // Issue #46 (registro 108): um chamado só pode ser de UM tipo por vez, o
  // manual da R12 (sem `provedor`) ou a corrida de um provedor integrado
  // (`useSimulacaoDeCorrida`, abaixo). Sem esta guarda os dois tiques disputam
  // o mesmo "procurando" e o achado fixo da R12 (`ACHADO_SIMULADO`) atropela o
  // entregador que a corrida simulada já tinha achado.
  const status = ehProvedorIntegrado(viagem.chamado?.provedor) ? null : viagem.chamado?.status
  useEffect(() => {
    if (status === 'procurando') {
      const id = setTimeout(() => atualizarChamado(viagem.id, 'achado', ACHADO_SIMULADO), 3000)
      return () => clearTimeout(id)
    }
    if (status === 'achado') {
      const id = setTimeout(() => atualizarChamado(viagem.id, 'chegou', viagem.chamado.entregador), 4000)
      return () => clearTimeout(id)
    }
    return undefined
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [status, viagem.id])
}

// Chamado antigo guardava só o nome em texto; o novo guarda o registro.
const entregadorDoChamado = (chamado) => (typeof chamado.entregador === 'string'
  ? { nome: chamado.entregador }
  : chamado.entregador)

// Issue #46 (registro 108): a MESMA ideia de `useSimulacaoDoChamado`, para a
// corrida de um provedor integrado (Lalamove, 99 Entregas). Tique de 3 s
// reais, não do relógio da simulação: PONTO ONDE O WEBHOOK REAL ENTRA
// (`infra/provedoresDeEntrega.js`, comentário do topo) — trocar este
// `setTimeout` pela chegada do evento do provedor. Para de tiquetaquear
// sozinho quando a corrida termina (entregue ou falha).
function useSimulacaoDeCorrida(viagem, avancarCorrida, agora) {
  const chamado = viagem.chamado
  const status = ehProvedorIntegrado(chamado?.provedor) ? chamado.status : null
  useEffect(() => {
    if (!status || corridaEhFinal(status)) return undefined
    const id = setTimeout(() => avancarCorrida(viagem.id, agora, chamado.provedor, status), 3000)
    return () => clearTimeout(id)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [status, viagem.id])
}

// Chip por chip, cotar e chamar (issue #46): fica num componente pequeno
// dentro do próprio arquivo (não precisa de arquivo novo) para o estado local
// de "qual provedor está cotando agora" não subir para `PainelViagem`, que já
// tem o suficiente para gerenciar.
function DespachoPorProvedor({ viagem, integracoesLogistica, agora, acoes }) {
  const [aberto, setAberto] = useState(false)
  const [provedorEscolhido, setProvedorEscolhido] = useState(null)
  const [cotacao, setCotacao] = useState(null)
  const [cotando, setCotando] = useState(false)

  const provedores = provedoresParaDespacho(integracoesLogistica)
  // Só "próprio" ligado: nada muda, o botão único de sempre (sem abrir chip
  // nenhum) — a Thati que nunca foi na aba de Integrações não vê UI nova.
  if (provedores.length <= 1) {
    return (
      <Botao variante="primario" onClick={() => acoes.chamarEntregador(viagem.id, agora)}>
        Chamar entregador
      </Botao>
    )
  }

  const pedidoDeReferencia = viagem.paradas[0]?.pedido
  const bairro = partesDoEndereco(viagem.paradas[0]?.cliente?.endereco).bairro

  const escolher = async (chave) => {
    setProvedorEscolhido(chave)
    setCotacao(null)
    if (chave === 'proprio') return
    setCotando(true)
    const resultado = await acoes.cotarEntrega(chave, pedidoDeReferencia, bairro)
    setCotacao(resultado)
    setCotando(false)
  }

  const confirmar = () => {
    if (provedorEscolhido === 'proprio') {
      acoes.chamarEntregador(viagem.id, agora)
    } else {
      acoes.chamarEntregadorComProvedor(viagem.id, agora, provedorEscolhido, cotacao, pedidoDeReferencia)
    }
    setAberto(false)
    setProvedorEscolhido(null)
    setCotacao(null)
  }

  if (!aberto) {
    return <Botao variante="primario" onClick={() => setAberto(true)}>Chamar entregador</Botao>
  }

  return (
    <EscolhaDeProvedorEntrega
      provedores={provedores}
      provedorEscolhido={provedorEscolhido}
      cotando={cotando}
      cotacaoTexto={cotacao ? `${moeda(cotacao.preco)} · ${plural(cotacao.prazoMin, 'minuto', 'minutos')}` : null}
      aviso={provedorEscolhido && provedorEscolhido !== 'proprio' ? AVISO_SIMULADO : null}
      aoEscolherProvedor={escolher}
      aoConfirmar={confirmar}
      aoFechar={() => { setAberto(false); setProvedorEscolhido(null); setCotacao(null) }}
    />
  )
}

export function PainelViagem({
  viagem, indice, janelas, agora, constantes, acoes, aoAbrirGerarRota, integracoesLogistica,
}) {
  const { setNodeRef, isOver } = useDroppable({
    id: 'viagem-' + viagem.id, data: { type: 'viagem', viagemId: viagem.id },
  })
  useSimulacaoDoChamado(viagem, acoes.atualizarChamado)
  useSimulacaoDeCorrida(viagem, acoes.avancarCorrida, agora)
  const corridaIntegrada = ehProvedorIntegrado(viagem.chamado?.provedor) ? viagem.chamado : null

  const paradasComFaixa = viagem.paradas.map((c) => ({
    id: c.id, faixa: janelas.find((j) => j.id === c.pedido.janela)?.faixa,
  }))
  const marcos = marcosDaViagem(paradasComFaixa, agora, constantes)
  const saiu = viagem.paradas.some((c) => indiceDoPasso(c.pedido.estado) >= indiceDoPasso('entrega'))
  const todasEntregues = viagem.paradas.every((c) => c.pedido.estado === 'entregue')
  const minutosParaSair = marcos.sair == null ? null : (marcos.sair - agora) / 60000

  return (
    <li
      ref={setNodeRef}
      className={`${css.viagem} ${isOver ? css.viagemSobre : ''}`}
      aria-label={'Viagem ' + indice}
    >
      <div className={css.cabecaViagem}>
        {!saiu && (
          <Anel tamanho={44} traco={5} fracao={Math.max(0, Math.min(1, (minutosParaSair ?? 0) / 60))} tom="ok">
            <span className={css.anelNumero}>
              {minutosParaSair == null ? '—' : Math.round(minutosParaSair)}
            </span>
          </Anel>
        )}
        <h3>Viagem {indice}</h3>
        <Pilula tom="neutro" fina>
          <Icone nome={viagem.modo === 'propria' ? 'house' : 'moto'} tamanho={16} />
          {viagem.modo === 'propria' ? 'Eu levo' : 'Entregador'}
        </Pilula>
        {todasEntregues && <Pilula tom="ok" fina>Concluída</Pilula>}
        {!saiu && marcos.sair != null && (
          <span className={css.saiViagem}>sai {horaCurta(new Date(marcos.sair).toISOString())}</span>
        )}
      </div>

      <ol className={css.paradas}>
        {viagem.paradas.map((conversa, indiceParada) => {
          const parada = marcos.porParada.find((p) => p.id === conversa.id)
          const bairro = partesDoEndereco(conversa.cliente?.endereco).bairro
          return (
            <li key={conversa.id} className={`${css.parada} ${parada?.atrasada ? css.paradaAtrasada : ''}`}>
              <span className={css.numeroParada}>{indiceParada + 1}.</span>
              <span className={css.nomeParada}>
                {conversa.nome} · {bairro || 'sem bairro'}
                {' · '}
                {parada?.chegadaReal != null && (
                  parada.atrasada
                    ? `chega ${horaCurta(new Date(parada.chegadaReal).toISOString())}, depois da janela`
                    : `chega ${horaCurta(new Date(parada.chegadaReal).toISOString())}`
                )}
              </span>
              {conversa.pedido.estado === 'entrega' && (
                <Botao onClick={() => acoes.marcarParadaEntregue(conversa.id, agora)}>Entregue</Botao>
              )}
              {!saiu && (
                <span className={css.botoesOrdem}>
                  <Botao
                    className={css.botaoOrdem} aria-label={'Subir ' + conversa.nome}
                    disabled={indiceParada === 0}
                    onClick={() => acoes.reordenarParada(viagem.id, indiceParada, indiceParada - 1)}
                  >
                    <Icone nome="chevron-up" tamanho={16} />
                  </Botao>
                  <Botao
                    className={`${css.botaoOrdem} ${css.baixo}`} aria-label={'Descer ' + conversa.nome}
                    disabled={indiceParada === viagem.paradas.length - 1}
                    onClick={() => acoes.reordenarParada(viagem.id, indiceParada, indiceParada + 1)}
                  >
                    <Icone nome="chevron-up" tamanho={16} />
                  </Botao>
                </span>
              )}
              <Botao variante="texto" onClick={() => acoes.tirarDaViagem(conversa.id)}>Tirar</Botao>
            </li>
          )
        })}
      </ol>

      {!saiu && (
        <div className={css.acoesViagem}>
          <Botao onClick={() => aoAbrirGerarRota(viagem)}>Gerar rota</Botao>
          {viagem.modo === 'entregador'
            ? (
              !viagem.chamado && (
                <DespachoPorProvedor
                  viagem={viagem} integracoesLogistica={integracoesLogistica} agora={agora} acoes={acoes}
                />
              )
            )
            : (
              <Botao variante="primario" onClick={() => acoes.sairParaEntrega(viagem.id, viagem.paradas.map((c) => c.id), agora)}>
                Sair agora
              </Botao>
            )}
          <Botao variante="texto" onClick={() => acoes.desfazerViagem(viagem.id)}>Desfazer viagem</Botao>
        </div>
      )}

      {corridaIntegrada && (
        <div className={css.chamado}>
          <span className={css.pillSimulado}>Simulado</span>
          <Pilula tom={PASSOS_CORRIDA[corridaIntegrada.status]?.tom ?? 'neutro'} fina>
            {PASSOS_CORRIDA[corridaIntegrada.status]?.rotulo ?? corridaIntegrada.status}
          </Pilula>
          <span>{resumoDaCorrida(corridaIntegrada, viagem.paradas[0]?.pedido, corridaIntegrada.provedor)}</span>
          {corridaIntegrada.entregador && corridaIntegrada.status !== 'entregue' && (
            <span>{textoDoEntregador({ tipo: 'plataforma', ...entregadorDoChamado(corridaIntegrada) })}</span>
          )}
          {(corridaIntegrada.status === 'procurando' || corridaIntegrada.status === 'a-caminho-coleta') && (
            <Botao
              variante="texto"
              onClick={() => acoes.cancelarCorrida(viagem.id, corridaIntegrada.provedor, corridaIntegrada.codigo)}
            >
              Cancelar corrida
            </Botao>
          )}
          {(corridaIntegrada.status === 'falha' || corridaIntegrada.status === 'cancelado') && (
            <Botao variante="primario" onClick={() => acoes.cancelarChamado(viagem.id)}>
              Escolher outro provedor
            </Botao>
          )}
        </div>
      )}

      {viagem.chamado && !corridaIntegrada && (
        <div className={css.chamado}>
          <span className={css.pillSimulado}>Simulado</span>
          {viagem.chamado.status === 'procurando' && (
            <>
              <span>Procurando entregador…</span>
              <Botao variante="texto" onClick={() => acoes.cancelarChamado(viagem.id)}>Cancelar chamado</Botao>
            </>
          )}
          {viagem.chamado.status === 'achado' && (
            <span>{textoDoEntregador({ tipo: 'plataforma', ...entregadorDoChamado(viagem.chamado) })} · chega {marcos.chamar != null ? horaCurta(new Date(marcos.sair).toISOString()) : ''}</span>
          )}
          {viagem.chamado.status === 'chegou' && (
            <>
              <span>Entregador chegou</span>
              <Botao
                variante="primario"
                onClick={() => acoes.sairParaEntrega(viagem.id, viagem.paradas.map((c) => c.id), agora)}
              >
                Entregar ao entregador
              </Botao>
            </>
          )}
          {viagem.chamado.status === 'em-rota' && <span>Em rota com {textoDoEntregador({ tipo: 'plataforma', ...entregadorDoChamado(viagem.chamado) })}.</span>}
        </div>
      )}

      {todasEntregues && (
        <p className={css.saiViagem}>Viagem {indice} concluída.</p>
      )}
    </li>
  )
}
