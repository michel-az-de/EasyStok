import { useEtiquetaDoMotor } from '../../../../aplicacao/etiquetaDoMotor'
import css from './etiqueta.module.css'

// #2247 (impressão v3, G1 item 7): a etiqueta de produção do Console é o PNG do motor do app, o
// mesmo canvas que a térmica recebe, em tamanho real (mm). A prévia e a folha mostram a mesma
// imagem; se o motor recusar (fonte que não carregou, alérgenos que não cabem), aparece o motivo
// e nada vai para o papel.
export function EtiquetaDeProducao({ etiqueta, empresa, total }) {
  const desenho = useEtiquetaDoMotor(etiqueta, empresa)
  const rotulo = `Etiqueta ${etiqueta.sequencial} de ${total}, lote ${etiqueta.lote}`
  if (desenho.carregando) return <output className={css.situacao}>Preparando a etiqueta…</output>
  if (desenho.recusa) return <p className={css.recusa} role="alert">Esta etiqueta não vai sair: {desenho.recusa}.</p>
  return (
    <img className={css.etiqueta} src={desenho.png} alt={rotulo}
      style={{ width: `${desenho.larguraMm}mm`, height: `${desenho.alturaMm}mm` }} />
  )
}
