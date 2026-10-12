// Resgate de pontos na ficha do cliente (frente Fidelidade e cupons, rodada
// 13, issue #45, registro 107). Só o mínimo no dia a dia (brief da rodada):
// saldo, catálogo de recompensas ativas e um botão por linha. Cadastro e
// edição do catálogo moram na Gestão (AbaFidelidade), não aqui.
import { useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { Icone } from '../../componentes/Icone'
import { Modal } from '../../componentes/Modal'
import { Pilula } from '../../componentes/Pilula'
import { Vazio } from '../../componentes/Vazio'
import { podeResgatar } from '../../dominio/fidelidade'
import css from './cliente.module.css'

const ICONE_POR_TIPO = { produto: 'package', 'frete-gratis': 'moto', sorteio: 'estrela' }

export function ModalFidelidade({
  nome, saldo, recompensas, sorteios, aoResgatar, aoFechar,
}) {
  const [resgatado, setResgatado] = useState(null)
  const ativas = (recompensas ?? []).filter((r) => r.ativo)

  function nomeDoSorteio(sorteioId) {
    return sorteios?.find((s) => s.id === sorteioId)?.nome ?? 'sorteio'
  }

  return (
    <Modal
      titulo={`Fidelidade de ${nome}`}
      descricao={`Saldo atual: ${saldo} ${saldo === 1 ? 'ponto' : 'pontos'}.`}
      aoFechar={aoFechar}
      rodape={<Botao onClick={aoFechar}>Fechar</Botao>}
    >
      {resgatado && (
        <output className={css.avisoEmLinha}>
          <Icone nome="circle-check" /> Resgate de "{resgatado}" registrado.
        </output>
      )}
      {ativas.length === 0 ? (
        <Vazio titulo="Nenhuma recompensa cadastrada">
          Cadastre em Gestão · Fidelidade e cupons.
        </Vazio>
      ) : (
        <ul className={css.listaRecompensas}>
          {ativas.map((recompensa) => {
            const pode = podeResgatar(recompensa, saldo)
            return (
              <li key={recompensa.id} className={css.linhaRecompensa}>
                <span className={css.iconeRecompensa} aria-hidden="true">
                  <Icone nome={ICONE_POR_TIPO[recompensa.tipo] ?? 'presente'} tamanho={18} />
                </span>
                <span className={css.textoRecompensa}>
                  <strong>{recompensa.rotulo}</strong>
                  {recompensa.tipo === 'sorteio' && (
                    <span className={css.subRecompensa}>{nomeDoSorteio(recompensa.sorteioId)}</span>
                  )}
                </span>
                <Pilula tom={pode ? 'ok' : 'neutro'} fina>{recompensa.custoPontos} pts</Pilula>
                <Botao
                  variante="secundario"
                  disabled={!pode}
                  title={pode ? undefined : 'Saldo insuficiente'}
                  onClick={() => { aoResgatar(recompensa); setResgatado(recompensa.rotulo) }}
                >
                  Resgatar
                </Botao>
              </li>
            )
          })}
        </ul>
      )}
    </Modal>
  )
}
