// Regras do baba-caixa sem E/S de protocolo: monta consultas e corpos para a API e exige confirmacao humana nas escritas.
const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const DATA = /^\d{4}-\d{2}-\d{2}$/;

export const qs = obj => {
  const p = Object.entries(obj).filter(([, v]) => v !== undefined && v !== null && v !== '');
  return p.length ? '?' + p.map(([k, v]) => `${k}=${encodeURIComponent(v)}`).join('&') : '';
};

function data(v, nome) { if (v !== undefined && !DATA.test(v)) throw new Error(`${nome} deve ser AAAA-MM-DD.`); return v; }
function uuid(v, nome) { if (v !== undefined && !UUID.test(v)) throw new Error(`${nome} deve ser um UUID.`); return v; }

export const consultas = {
  dia: ({ data: d, loja_id }) => '/api/caixa/dia' + qs({ data: data(d, 'data'), lojaId: uuid(loja_id, 'loja_id') }),
  movimentos: ({ desde, ate, tipo, pagina = 1, por_pagina = 50 }) =>
    '/api/caixa/movimentos' + qs({ desde: data(desde, 'desde'), ate: data(ate, 'ate'), tipo, page: pagina, pageSize: Math.min(por_pagina, 100) }),
  linhas: ({ desde, ate }) => '/api/caixa/linhas' + qs({ desde: data(desde, 'desde'), ate: data(ate, 'ate') }),
  fechamentos: ({ desde, ate, pagina = 1, por_pagina = 30 }) =>
    '/api/caixa/fechamentos' + qs({ desde: data(desde, 'desde'), ate: data(ate, 'ate'), page: pagina, pageSize: Math.min(por_pagina, 100) }),
};

/** Corpos de escrita. Identidade, origem e empresa vem do token no servidor da API, nunca daqui. */
export const escritas = {
  abrir: ({ saldo_inicial = 0, loja_id, observacoes }) => ({
    resumo: `ABRIR caixa com saldo inicial R$ ${Number(saldo_inicial).toFixed(2)}`,
    caminho: '/api/caixa/abrir', corpo: { saldoInicial: saldo_inicial, lojaId: uuid(loja_id, 'loja_id'), observacoes },
  }),
  fechar: ({ data: d, valor_contado, loja_id, observacoes }) => ({
    resumo: `FECHAR caixa${d ? ' de ' + d : ' de hoje'}${valor_contado !== undefined ? ` contado R$ ${Number(valor_contado).toFixed(2)}` : ''}`,
    caminho: '/api/caixa/fechar', corpo: { data: data(d, 'data'), valorContado: valor_contado, lojaId: uuid(loja_id, 'loja_id'), observacoes },
  }),
  movimento: ({ tipo, valor, descricao, metodo, categoria, loja_id }) => {
    if (!['entrada', 'saida'].includes(tipo)) throw new Error('tipo deve ser entrada ou saida.');
    if (!(valor > 0)) throw new Error('valor deve ser maior que zero.');
    return {
      resumo: `LANCAR ${tipo.toUpperCase()} de R$ ${Number(valor).toFixed(2)}${descricao ? ` (${descricao})` : ''}`,
      caminho: '/api/caixa/movimentos', corpo: { tipo, valor, descricao, metodo, categoria, lojaId: uuid(loja_id, 'loja_id') },
    };
  },
};

/** Escrita so com BABA_CAIXA_ESCRITA=1 e confirmacao da pessoa pelo cliente (fora do canal do modelo). */
export async function escrever({ env = process.env, cliente, pedir, plano }) {
  if (env.BABA_CAIXA_ESCRITA !== '1') throw new Error('escrita desligada: defina BABA_CAIXA_ESCRITA=1 na sua maquina.');
  const ok = await pedir(`Confirma na Casa da Baba?\n${plano.resumo}`);
  if (!ok) return { feito: false, motivo: 'nao confirmado pela pessoa' };
  const corpo = Object.fromEntries(Object.entries(plano.corpo).filter(([, v]) => v !== undefined));
  return { feito: true, resposta: await cliente.post(plano.caminho, corpo) };
}
