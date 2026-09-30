import { useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { CampoArea, CampoTexto } from '../../componentes/Campo'
import { Modal } from '../../componentes/Modal'
import { Pilula } from '../../componentes/Pilula'
import { useAcoes, useAtendimento, useCatalogo } from '../../aplicacao/contextos'
import { VARIAVEIS, contextoDePrevia, textoDaRegra } from '../../dominio/automacao'
import { ROTULO_DO_SOM, listaDeEventosDeSom } from '../../dominio/automatico'
import { canalDaConversa } from '../../dominio/canal'
import { DIAS_DA_SEMANA, descreverProximaAbertura } from '../../dominio/funcionamento'
import { faixaDaJanela } from '../../dominio/entrega'
import { permissaoDeEscrita } from '../../dominio/janela'
import css from './automacoes.module.css'

const AVISOS_DE_SOM = listaDeEventosDeSom().map((e) => ROTULO_DO_SOM[e]).join(', ')

function Regra({
  regra, contexto, aoAlternar, aoEditar, nomeConversa, podeEnviar, aoEnviar,
}) {
  const [rascunho, setRascunho] = useState(null)

  const valor = rascunho ?? { texto: regra.texto }
  const editando = valor.texto !== regra.texto
  const previa = textoDaRegra({ ...regra, texto: valor.texto }, contexto)

  const mudar = (campo, novo) => setRascunho({ ...valor, [campo]: novo })

  const salvar = () => {
    aoEditar(regra.id, { texto: valor.texto.trim() })
    setRascunho(null)
  }

  return (
    <li className={`${css.regra} ${regra.ativa ? '' : css.desligada}`}>
      <div className={css.topo}>
        <strong>{regra.nome}</strong>
        {/* Chave com rótulo de estado. Ligado e desligado não podem ser
            parecidos: o rótulo diz, a cor só acompanha. */}
        <label className={css.chave}>
          <input
            type="checkbox"
            aria-label={`Regra ${regra.nome}, ${regra.ativa ? 'ligada' : 'desligada'}`}
            checked={regra.ativa}
            onChange={() => aoAlternar(regra.id)}
          />
          {regra.ativa ? 'ligada' : 'desligada'}
        </label>
      </div>
      <p className={css.descricao}>{regra.descricao}</p>

      <CampoArea
        rotulo={`Texto de ${regra.nome}`}
        value={valor.texto}
        onChange={(e) => mudar('texto', e.target.value)}
      />
      <p className={css.variaveis}>
        <span>Variáveis:</span>
        {VARIAVEIS.map((v) => <span key={v.chave} className={css.tokenVariavel}>{`{${v.chave}}`}</span>)}
      </p>

      {/* Prévia em bolha: é o que o cliente lê agora, com os dados desta conversa. */}
      <p className={css.rotuloPrevia}>O cliente recebe assim agora:</p>
      <p className={css.bolha}>{previa}</p>

      {editando && (
        <p className={css.editando}>
          <Pilula tom="aviso" fina>editando, não salvo</Pilula>
        </p>
      )}

      <div className={css.acoes}>
        <Botao variante="primario" className={css.toque} disabled={!editando} onClick={salvar}>
          Salvar texto
        </Botao>
        <Botao className={css.toque} disabled={!editando} onClick={() => setRascunho(null)}>
          Desfazer
        </Botao>
        {/* Achado 1, P0 (banca 10): a tela que o dono já achou (trilho,
            rótulo por extenso) não deixava enviar; quem enviava de fato era
            outro ícone, escondido dentro da conversa. Mesma ação `enviar` de
            useAcoes() que features/respostas/ModalBiblioteca.jsx já usa. */}
        <Botao
          icone="enviar"
          className={css.toque}
          disabled={!podeEnviar}
          title={!podeEnviar ? 'Abra uma conversa (com janela aberta para texto livre) para enviar.' : undefined}
          onClick={() => aoEnviar(regra, previa)}
        >
          {nomeConversa ? `Enviar para ${nomeConversa.split(' ')[0]}` : 'Enviar'}
        </Botao>
      </div>
    </li>
  )
}

// Uma linha por dia da semana (pedido do dono, 24/09/2026): fechado o dia
// todo tira os dois campos de hora, sem apagar o que estava neles (ela pode
// desmarcar e achar o horário de antes do jeito que deixou).
//
// Achado 8 (banca capricho R10): as sete linhas competiam em pé de igualdade,
// sem nada que dissesse qual vale hoje. `hoje` já vem calculado do relógio da
// tela (ModalAutomacoes), esta função só decora a linha.
function DiaDeFuncionamento({ dia, valor, aoEditar, hoje }) {
  return (
    <li className={`${css.diaFuncionamento} ${hoje ? css.diaFuncionamentoHoje : ''}`}>
      <strong>{dia.rotulo}</strong>
      {hoje && <span className={css.marcaHoje}>Hoje</span>}
      <label className={css.chave}>
        <input
          type="checkbox"
          aria-label={`${dia.rotulo}, fechado o dia todo`}
          checked={valor.fechado}
          onChange={(e) => aoEditar({
            fechado: e.target.checked,
            ...(!e.target.checked && !valor.abre ? { abre: '08:00', fecha: '18:00' } : {}),
          })}
        />
        Fechado o dia todo
      </label>
      {!valor.fechado && (
        <span className={css.horariosDoDia}>
          <CampoTexto
            rotulo={`Abre, ${dia.rotulo}`}
            rotuloOculto
            tipo="time"
            value={valor.abre ?? ''}
            onChange={(e) => aoEditar({ abre: e.target.value })}
          />
          <CampoTexto
            rotulo={`Fecha, ${dia.rotulo}`}
            rotuloOculto
            tipo="time"
            value={valor.fecha ?? ''}
            onChange={(e) => aoEditar({ fecha: e.target.value })}
          />
        </span>
      )}
    </li>
  )
}

export function ModalAutomacoes({ regras, aoAlternar, aoFechar }) {
  const {
    selecionada, agora, som, audioBloqueado, funcionamento, aberta, lojaAberta, permissaoNotificacao,
  } = useAtendimento()
  const {
    editarRegra, alternarSom, ouvirAmostraDeSom, editarFuncionamento, alternarLoja, pedirNotificacaoDoNavegador,
    enviar,
  } = useAcoes()
  const { janelas, canais } = useCatalogo()

  const faixa = faixaDaJanela(janelas, selecionada?.pedido?.janela)
  // `abre` só a regra "Fora do horário" usa (VARIAVEIS, dominio/automacao.js);
  // as outras ignoram a chave que não citam no texto.
  const contexto = { ...contextoDePrevia(selecionada, faixa), abre: descreverProximaAbertura(agora, funcionamento) }
  // Achado 8: mesmo relógio de `agora` que já decide Aberta/Fechada aqui do
  // lado, só lido como dia da semana (0-6, igual Date#getDay) para achar a
  // chave em DIAS_DA_SEMANA.
  const chaveDeHoje = DIAS_DA_SEMANA[new Date(agora).getDay()].chave

  // Mesma conta de canal/janela do composer e da biblioteca (Respostas):
  // enviar daqui respeita a mesma regra de janela fechada, nunca um atalho
  // que fura o que o resto do app trava.
  const canal = selecionada ? canalDaConversa(canais, selecionada) : null
  const { pode: podeEnviar } = selecionada
    ? permissaoDeEscrita(selecionada, agora, canal)
    : { pode: false }

  function enviarRegra(regra, previa) {
    enviar(selecionada.id, previa, { automatica: true, regra: regra.id })
    aoFechar()
  }

  return (
    <Modal
      titulo="Mensagens automáticas"
      descricao="O que a casa fala sozinha, em nome dela."
      aoFechar={aoFechar}
      rodape={<Botao className={css.toque} onClick={aoFechar}>Fechar</Botao>}
    >
      {/* Horário de funcionamento e estado da loja (pedido do dono,
          24/09/2026): o controle do topo já mostra Aberta/Fechada sempre;
          aqui é onde ela configura o horário por dia e força a exceção do
          dia, sem precisar abrir outra tela. */}
      <div className={css.funcionamento}>
        <div className={css.topo}>
          <strong>Horário de funcionamento</strong>
          <Pilula tom={aberta ? 'ok' : 'perigo'}>{aberta ? 'Aberta agora' : 'Fechada agora'}</Pilula>
        </div>
        <p className={css.descricao}>
          {lojaAberta == null
            ? 'Segue o horário configurado abaixo.'
            : `Forçada ${lojaAberta ? 'aberta' : 'fechada'} na mão, por cima do horário.`}
        </p>
        <Botao className={css.toque} onClick={() => alternarLoja(agora)}>
          {aberta ? 'Fechar loja agora' : 'Abrir loja agora'}
        </Botao>
        <ul className={css.diasFuncionamento}>
          {DIAS_DA_SEMANA.map((dia) => (
            <DiaDeFuncionamento
              key={dia.chave}
              dia={dia}
              valor={funcionamento[dia.chave]}
              aoEditar={(campos) => editarFuncionamento(dia.chave, campos)}
              hoje={dia.chave === chaveDeHoje}
            />
          ))}
        </ul>
      </div>

      {/* Som da cozinha: liga e desliga aqui, porque é aqui que mora o que a
          casa faz sozinha. Sons sintetizados em infra/som.js, sem arquivo. */}
      <div className={css.som}>
        <label className={css.chave}>
          <input
            type="checkbox"
            aria-label={`Som da cozinha, ${som ? 'ligado' : 'desligado'}`}
            checked={som}
            onChange={(e) => alternarSom(e.target.checked)}
          />
          Som da cozinha {som ? 'ligado' : 'desligado'}
        </label>
        <p className={css.descricao}>
          Avisa em {AVISOS_DE_SOM}.
        </p>
        {som && audioBloqueado && (
          <output className={css.avisoAudio}>
            Toque em qualquer lugar da tela para ligar o som.
          </output>
        )}
        <div className={css.amostras}>
          {listaDeEventosDeSom().map((evento) => (
            <Botao
              key={evento}
              variante="texto"
              icone="play"
              className={css.amostra}
              onClick={() => ouvirAmostraDeSom(evento)}
            >
              Ouvir: {ROTULO_DO_SOM[evento]}
            </Botao>
          ))}
        </div>
      </div>

      {/* Notificação do navegador (rodada 10, item 6): canal fora da aba
          para os motivos de "Precisa de você" (US-013/UC-02, "todos os
          dispositivos logados"; pedido literal do dono, "não tem
          notificações de sistema"). Permissão só por este clique dela,
          nunca pedida sozinha. */}
      <div className={css.som}>
        <div className={css.topo}>
          <strong>Notificação do navegador</strong>
          <Pilula tom={permissaoNotificacao === 'granted' ? 'ok' : 'neutro'}>
            {permissaoNotificacao === 'granted' && 'Ativada'}
            {permissaoNotificacao === 'denied' && 'Bloqueada'}
            {permissaoNotificacao === 'default' && 'Desativada'}
            {permissaoNotificacao === 'indisponivel' && 'Sem suporte'}
          </Pilula>
        </div>
        <p className={css.descricao}>
          Avisa "Precisa de você" mesmo com a aba minimizada ou outra janela em foco.
        </p>
        {permissaoNotificacao === 'default' && (
          <Botao className={css.toque} icone="globo" onClick={pedirNotificacaoDoNavegador}>
            Ativar notificação do navegador
          </Botao>
        )}
        {permissaoNotificacao === 'denied' && (
          <p className={css.descricao}>Bloqueada nas configurações do navegador.</p>
        )}
      </div>

      <ul className={css.lista}>
        {regras.map((regra) => (
          <Regra
            key={regra.id}
            regra={regra}
            contexto={contexto}
            aoAlternar={aoAlternar}
            aoEditar={editarRegra}
            nomeConversa={selecionada?.nome}
            podeEnviar={podeEnviar}
            aoEnviar={enviarRegra}
          />
        ))}
      </ul>
    </Modal>
  )
}
