import { useEffect, useState } from 'react'
import { Avatar } from '../../componentes/Avatar'
import { Bloco } from '../../componentes/Bloco'
import { Botao } from '../../componentes/Botao'
import { CampoMascarado } from '../../componentes/CampoMascarado'
import { CampoTexto } from '../../componentes/Campo'
import { Icone } from '../../componentes/Icone'
import { Popover } from '../../componentes/Popover'
import { useAcoes, useAtendimento, useCatalogo } from '../../aplicacao/contextos'
import { useAcaoDisponivel } from '../../aplicacao/useAcaoDisponivel'
import {
  bloqueioDe, conversasDoCadastro, ehLead, estaBloqueada, resumoDoBloqueio,
} from '../../dominio/conversa'
import { canalDaConversa, fotoDoCliente } from '../../dominio/canal'
import {
  listaEmPortugues, mascaraCep, mascaraTelefone, partesDoEndereco, moeda,
} from '../../dominio/formato'
import { indiceDoPasso } from '../../dominio/esteira'
import { numeroCurto, pedidoEncerrado } from '../../dominio/pedido'
import { SITUACOES, situacaoDoCep } from '../../dominio/areaEntrega'
import { chegouQuando, dicaDoCep, mesmoDomicilio, resumoFinanceiro } from '../../dominio/cliente'
import { SELO_CAPTADO, automaticoConduzindo } from '../../dominio/captura'
import { historicoElegivelParaFidelidade, saldoDePontos } from '../../dominio/fidelidade'
import { notasRecentes } from '../../dominio/notas'
import { CampoEmLinha } from './CampoEmLinha'
import { TagsDoCliente } from './TagsDoCliente'
import { ModalHistorico } from './ModalHistorico'
import { ModalBloqueio, ModalDesbloqueio } from './ModalBloqueio'
import { ModalFidelidade } from './ModalFidelidade'
import { AvisosDoCliente } from './AvisosDoCliente'
import css from './cliente.module.css'

const dataCurta = (iso) =>
  new Date(iso + (iso.length === 10 ? 'T12:00:00' : '')).toLocaleDateString('pt-BR', { day: '2-digit', month: 'short' })

// Estado inicial do formulário de cadastro rápido (seção 2): nome vem do
// perfil do canal, telefone e endereço vêm do que a conversa já capturou.
// "Endereço" é um campo só na tela (rua, número, complemento e bairro juntos,
// sem o CEP): `partesDoEndereco` separa porque o CEP precisa da própria
// validação de área, o resto não.
function estadoInicialDoForm(conversa) {
  const partes = partesDoEndereco(conversa.cliente.enderecoCapturado ?? '')
  const enderecoSemCep = [partes.rua, partes.numero, partes.complemento, partes.bairro]
    .filter(Boolean).join(', ')
  return {
    nome: conversa.nome,
    telefone: conversa.cliente.telefone ?? '',
    enderecoSemCep,
    cep: partes.cep ? mascaraCep(partes.cep) : '',
  }
}

// Rodada 11 (issue #8, registro 92): selo do campo que o automático captou
// da conversa. A dona vê de onde veio o dado sem abrir a conversa (US-017),
// e confere antes de usar (o campo continua editável ao lado).
function SeloCaptado({ quando }) {
  if (!quando) return null
  return (
    <span className={css.seloCaptado} title={`Captado pelo automático às ${quando.slice(11, 16)}`}>
      <Icone nome="raio" tamanho={12} /> {SELO_CAPTADO}
    </span>
  )
}

export function BlocoCliente({ conversa }) {
  const { cliente } = conversa
  const { conversas, historico, agora, automaticoPausado, fidelidade, fonteApi } = useAtendimento()
  // #1474 (R2): no modo API, mudar a entrega do pedido e os pontos não têm endpoint e somem.
  const disponivel = useAcaoDisponivel()
  const { canais, motivosBloqueio, prefixosCepAtendidos } = useCatalogo()
  const {
    bloquearCliente, desbloquearCliente, selecionar,
    salvarCadastroRapido, editarDadoCliente, mudarEnderecoDoPedido, resgatarRecompensa,
  } = useAcoes()

  const [modal, setModal] = useState(null)
  const [menuAberto, setMenuAberto] = useState(false)
  const [avisoEndereco, setAvisoEndereco] = useState(null)
  const [form, setForm] = useState(() => estadoInicialDoForm(conversa))

  // Troca de conversa: recarrega o rascunho do cadastro rápido e limpa avisos
  // que eram da conversa anterior. Só depende do id: editar o formulário não
  // pode disparar este reset a cada tecla.
  useEffect(() => {
    setForm(estadoInicialDoForm(conversa))
    setAvisoEndereco(null)
    setMenuAberto(false)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [conversa.id])

  const bloqueada = estaBloqueada(conversa)
  const bloqueio = bloqueioDe(conversa)
  // Rodada 5 (seção 2, passo zero): conversasDoCadastro passou a comparar
  // cadastroId, não nome (RN-14 sobrevive a renomear o cliente).
  const doCadastro = conversasDoCadastro(conversas, conversa.cadastroId).length
  const nomesDeCanal = listaEmPortugues(canais.map((c) => c.nome))
  const canal = canalDaConversa(canais, conversa)
  const lead = ehLead(conversa)
  // P2 (banca3/e1-e3, achado 2; decisão 34): com o endereço capturado
  // pendente, o banner "Endereço capturado" já é o caminho para cadastrar.
  // Mostrar TAMBÉM o formulário completo aqui virava dois blocos cadastrando
  // ao mesmo tempo, sem um saber do outro. Com o banner ativo, esta seção cai
  // para os campos comuns (linha 214 adiante), editáveis um a um.
  //
  // Rodada 11 (registro 92): com o automático montando o cadastro pela
  // conversa (`conversa.captura`), a ficha mostra as linhas de dado, que
  // preenchem à vista campo a campo com o selo "captado da conversa", em vez
  // do formulário vazio esperando a dona digitar o que o cliente já disse.
  const captado = cliente.captado ?? {}
  const automaticoCaptando = automaticoConduzindo(conversa, automaticoPausado[conversa.id])
  const precisaCadastro = lead && (!cliente.telefone || !cliente.endereco) && !cliente.enderecoCapturado
    && !conversa.captura
  const domicilio = mesmoDomicilio(conversa, conversas)
  const financeiro = resumoFinanceiro(historico)

  // Frente Fidelidade e cupons (rodada 13, issue #45, registro 107): saldo
  // DERIVADO do histórico pago (ver dominio/fidelidade.js), nunca um contador
  // à parte. Lead ainda não comprou, então não tem saldo (mesmo corte de
  // `financeiro`/`numerosCliente` abaixo).
  const pedidosElegiveis = historicoElegivelParaFidelidade(historico, conversa.pedido)
  const resgatesDoCadastro = fidelidade.resgatesPorCadastro[conversa.cadastroId] ?? []
  const saldoPontos = saldoDePontos(pedidosElegiveis, resgatesDoCadastro, fidelidade.config)

  // Achado 5 (banca capricho R10): com "Endereço capturado" pendente lá em
  // cima, esta linha mostrava "Adicionar endereço" como se nada tivesse
  // chegado, as duas verdades sem nenhuma ligação visual. Mesmo cálculo do
  // banner (linha 78 abaixo).
  const enderecoPendente = Boolean(cliente.enderecoCapturado) && !cliente.endereco
  const situacaoCep = form.cep ? situacaoDoCep(form.cep, prefixosCepAtendidos) : SITUACOES.SEM_CEP
  const cepForaDaArea = situacaoCep === SITUACOES.FORA || situacaoCep === SITUACOES.LIMITE
  const podeSalvarCadastro = form.nome.trim() && form.telefone.replace(/\D/g, '').length >= 10

  function aoSalvarCadastro() {
    const endereco = [form.enderecoSemCep.trim(), form.cep ? mascaraCep(form.cep) : '']
      .filter(Boolean).join(', ') || null
    salvarCadastroRapido(conversa.id, { nome: form.nome, telefone: form.telefone, endereco }, agora)
  }

  const validarNome = (v) => (v.trim() ? null : 'Escreva o nome')
  const validarTelefone = (v) => (v.replace(/\D/g, '').length >= 10 ? null : 'Telefone com DDD')

  function aoSalvarEndereco(valor) {
    editarDadoCliente(conversa.id, 'endereco', valor, agora)
    // Modo API: rascunho de comanda leva o endereço do cadastro quando vira pedido, então não
    // há o que perguntar; pedido já criado segue com o endereço antigo (sem endpoint para trocar).
    if (fonteApi && !conversa.pedido?.pedidoId) return
    if (conversa.pedido && !pedidoEncerrado(conversa.pedido)) {
      const antesDeEntrega = indiceDoPasso(conversa.pedido.estado) < indiceDoPasso('entrega')
      setAvisoEndereco(antesDeEntrega && disponivel('mudarEnderecoDoPedido') ? 'perguntar' : 'aviso')
    }
  }

  // #1442: quatro seções que recolhem e lembram (Bloco com `chave`), para a
  // rolagem da ficha não ficar extensa. Recolhida, cada uma deixa um resumo.
  const resumoTags = cliente.tags.length === 1 ? '1 tag' : `${cliente.tags.length} tags`
  const resumoHistorico = cliente.pedidos === 1 ? '1 pedido' : `${cliente.pedidos} pedidos`

  return (
    <>
      <Bloco titulo="Cliente" chave="cliente" resumo={conversa.nome}>
        <div className={css.identidade}>
          <Avatar nome={conversa.nome} foto={fotoDoCliente(canal, cliente)} tamanho="enorme" />
          <div className={css.identidadeTextos}>
            <div key={`nome-${captado.nome ?? ''}`} className={captado.nome ? css.captadoAgora : undefined}>
              <CampoEmLinha rotulo="Nome do cliente" valor={conversa.nome} validar={validarNome}
                aoSalvar={(v) => editarDadoCliente(conversa.id, 'nome', v.trim(), agora)} />
              <SeloCaptado quando={captado.nome} />
            </div>
            {lead ? (
              <span className={css.situacao}>Lead · {chegouQuando(conversa, agora)}</span>
            ) : cliente.desde && (
              // #1474: o cadastro da API não traz a data; sem ela, a linha não aparece vazia.
              <span className={css.situacao}>Cliente desde {cliente.desde}</span>
            )}
          </div>
          <span className={css.menuCliente}>
            <Botao
              className={css.gatilhoMenu}
              aria-haspopup="menu"
              aria-expanded={menuAberto}
              aria-label="Mais ações do cliente"
              title="Mais ações do cliente"
              onClick={() => setMenuAberto((v) => !v)}
            >
              <Icone nome="ellipsis" tamanho={24} />
            </Botao>
            {menuAberto && (
              <Popover rotulo="Mais ações do cliente" posicao="abaixo" aoFechar={() => setMenuAberto(false)}>
                <div className={css.menuAcoes}>
                  <button
                    type="button" role="menuitem" className={css.itemMenu}
                    onClick={() => { setMenuAberto(false); setModal(bloqueada ? 'desbloquear' : 'bloquear') }}
                  >
                    <Icone nome="bloqueio" />
                    {bloqueada ? 'Desbloquear cliente' : 'Bloquear cliente'}
                  </button>
                </div>
              </Popover>
            )}
          </span>
        </div>

        {/* Sinal forte de bloqueio: ícone, palavra e motivo. A cor sozinha não
            informa nada, então o texto diz tudo o que o vermelho sugeriria. */}
        {bloqueada && (
          <div className={css.avisoBloqueio}>
            <p className={css.tituloBloqueio}>
              <Icone nome="bloqueio" />
              <strong>Cadastro bloqueado</strong>
              <span className={css.alcanceBloqueio}>vale em todos os canais</span>
            </p>
            <p className={css.motivoBloqueio}>{bloqueio.motivo}</p>
            <p className={css.rodapeBloco}>{resumoDoBloqueio(bloqueio)}</p>
          </div>
        )}
      </Bloco>

      <Bloco titulo="Contato e endereço" chave="contato" resumo={cliente.telefone ?? ''}>
        {precisaCadastro ? (
          <div className={css.cadastroRapido}>
            {!cliente.telefone && (
              <CampoMascarado
                tipo="telefone" rotulo="Telefone"
                valor={form.telefone}
                aoMudarDigitos={(d) => setForm((f) => ({ ...f, telefone: mascaraTelefone(d) }))}
                placeholder="(11) 98765-4321"
              />
            )}
            {!cliente.endereco && (
              <>
                <CampoTexto
                  rotulo="Endereço"
                  value={form.enderecoSemCep}
                  onChange={(e) => setForm((f) => ({ ...f, enderecoSemCep: e.target.value }))}
                  placeholder="Rua, número, complemento e bairro"
                  dica={cliente.enderecoCapturado ? 'Lido da conversa' : undefined}
                />
                <CampoMascarado
                  tipo="cep" rotulo="CEP"
                  valor={form.cep}
                  aoMudarDigitos={(d) => setForm((f) => ({ ...f, cep: mascaraCep(d) }))}
                  placeholder="00000-000"
                  dica={dicaDoCep(cepForaDaArea, { fonteApi })}
                />
              </>
            )}
            <Botao largo variante="primario" icone="user-plus" disabled={!podeSalvarCadastro} onClick={aoSalvarCadastro}>
              Salvar cadastro
            </Botao>
          </div>
        ) : (
          <div className={css.dadosCliente}>
            <div key={`tel-${captado.telefone ?? ''}`} className={`${css.linhaDado} ${captado.telefone ? css.captadoAgora : ''}`}>
              <span className={`${css.azulejo} ${css.azVerde}`} aria-hidden="true"><Icone nome="phone" tamanho={15} /></span>
              <span className={css.rotuloDado}>Telefone <SeloCaptado quando={captado.telefone} /></span>
              <CampoEmLinha rotulo="Telefone" valor={cliente.telefone}
                vazio={automaticoCaptando ? 'O automático está pedindo na conversa' : 'Adicionar telefone'} tipo="telefone"
                validar={validarTelefone} aoSalvar={(v) => editarDadoCliente(conversa.id, 'telefone', v, agora)} />
            </div>
            <div
              key={`end-${captado.endereco ?? ''}`}
              className={`${css.linhaDado} ${enderecoPendente ? css.linhaDadoPendente : ''} ${captado.endereco ? css.captadoAgora : ''}`}
            >
              <span className={`${css.azulejo} ${css.azAzul}`} aria-hidden="true"><Icone nome="map-pin" tamanho={15} /></span>
              <span className={css.rotuloDado}>Endereço <SeloCaptado quando={captado.endereco} /></span>
              <CampoEmLinha rotulo="Endereço" valor={cliente.endereco}
                vazio={enderecoPendente ? 'Aguardando confirmação acima'
                  : automaticoCaptando ? 'O automático está pedindo na conversa' : 'Adicionar endereço'}
                aoSalvar={aoSalvarEndereco} />
            </div>
            <div className={css.linhaDado}>
              <span className={`${css.azulejo} ${css['az_' + canal.icone] ?? css.azCinza}`} aria-hidden="true"><Icone nome={canal.icone} tamanho={15} /></span>
              <span className={css.rotuloDado}>Canal</span>
              <span className={css.valorCanal}>
                {canal.nome}{cliente.usuario ? ` · @${cliente.usuario}` : ''}
              </span>
            </div>
            {/* Canais de aviso: e-mail e SMS (AvisosDoCliente.jsx, que no modo API
                grava o consentimento do cadastro, F02). */}
            <div className={css.linhaDado}>
              <span className={css.rotuloDado}>Avisos</span>
              <span className={css.avisosCanais}>
                <AvisosDoCliente conversa={conversa} />
              </span>
            </div>

            {avisoEndereco === 'perguntar' && (
              <p className={css.avisoEmLinha}>
                Mudar também a entrega do pedido {numeroCurto(conversa.pedido.numero)}?
                <Botao variante="secundario" onClick={() => { mudarEnderecoDoPedido(conversa.id, agora); setAvisoEndereco(null) }}>
                  Mudar entrega
                </Botao>
                <Botao variante="texto" onClick={() => setAvisoEndereco(null)}>Só o cadastro</Botao>
              </p>
            )}
            {avisoEndereco === 'aviso' && (
              <p className={css.avisoEmLinha}>
                {indiceDoPasso(conversa.pedido.estado) < indiceDoPasso('entrega')
                  ? `O cadastro mudou; o pedido ${numeroCurto(conversa.pedido.numero)} segue com o endereço antigo.`
                  : `Pedido ${numeroCurto(conversa.pedido.numero)} já saiu com o endereço antigo.`}
              </p>
            )}
          </div>
        )}

        {domicilio && (
          <p className={css.mesmoDomicilio}>
            Mesmo endereço de{' '}
            <button type="button" className={css.linkDomicilio} onClick={() => selecionar(domicilio.id)}>
              {domicilio.nome}
            </button>
          </p>
        )}
      </Bloco>

      <Bloco titulo="Tags" chave="tags" resumo={resumoTags}>
        <TagsDoCliente conversa={conversa} />

        {/* #1441: as notas internas mais recentes como post-its, ao lado da conversa. */}
        {cliente.notas.length > 0 && (
          <ul className={css.postIts} aria-label="Notas internas">
            {notasRecentes(cliente.notas).map((nota) => (
              <li key={nota.id ?? nota.em + nota.texto.slice(0, 12)} className={css.ultimaNota}>
                <span className={css.rotuloNota}>Nota · {nota.em.slice(0, 5)}{nota.autor ? ` · ${nota.autor}` : ''}</span>
                <span className={css.textoNota}>{nota.texto}</span>
              </li>
            ))}
          </ul>
        )}
      </Bloco>

      <Bloco titulo="Histórico" chave="historico" resumo={resumoHistorico}>
        {!lead && cliente.pedidos > 0 && (
          <div className={css.numerosCliente}>
            <span>
              <b>{cliente.pedidos}</b>
              <i>{cliente.pedidos === 1 ? 'pedido' : 'pedidos'}</i>
            </span>
            {/* Sem histórico carregado não há soma: "R$ 0,00" dizia um valor falso. */}
            {financeiro.total > 0 && (
              <span>
                <b>{moeda(financeiro.total)}</b>
                <i>valor total</i>
              </span>
            )}
            {financeiro.ultimoEm && (
              <span>
                <b>{dataCurta(financeiro.ultimoEm)}</b>
                <i>último pedido</i>
              </span>
            )}
          </div>
        )}

        {/* Selo discreto de fidelidade (pedido do Felipe: "sem sujar a UX
            atual"): só saldo e o botão de resgate, o catálogo mora na Gestão. */}
        {!lead && disponivel('resgatarRecompensa') && (
          <div className={css.linhaFidelidade}>
            <span className={css.seloPontos}>
              <Icone nome="estrela" tamanho={13} /> {saldoPontos} {saldoPontos === 1 ? 'ponto' : 'pontos'}
            </span>
            <Botao variante="texto" onClick={() => setModal('fidelidade')}>Resgatar</Botao>
          </div>
        )}

        <Botao largo icone="historico" onClick={() => setModal('historico')}>
          Histórico · {cliente.pedidos}
        </Botao>
      </Bloco>

        {modal === 'bloquear' && (
          <ModalBloqueio
            nome={conversa.nome}
            canais={nomesDeCanal}
            quantasConversas={doCadastro}
            motivos={motivosBloqueio}
            aoFechar={() => setModal(null)}
            aoConfirmar={(motivo) => { bloquearCliente(conversa.nome, motivo); setModal(null) }}
          />
        )}

        {modal === 'desbloquear' && (
          <ModalDesbloqueio
            nome={conversa.nome}
            aoFechar={() => setModal(null)}
            aoConfirmar={() => { desbloquearCliente(conversa.nome); setModal(null) }}
          />
        )}

        {modal === 'historico' && (
          <ModalHistorico
            conversa={conversa}
            historico={historico}
            agora={agora}
            aoFechar={() => setModal(null)}
          />
        )}

        {modal === 'fidelidade' && (
          <ModalFidelidade
            nome={conversa.nome}
            saldo={saldoPontos}
            recompensas={fidelidade.recompensas}
            sorteios={fidelidade.sorteios}
            aoResgatar={(recompensa) => resgatarRecompensa(
              conversa.cadastroId, conversa.nome, recompensa.id, saldoPontos, agora,
            )}
            aoFechar={() => setModal(null)}
          />
        )}
    </>
  )
}
