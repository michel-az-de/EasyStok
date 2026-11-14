// Regras do baba-caixa sem E/S de protocolo: monta consultas e corpos para a API e exige confirmacao humana nas escritas.
const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const DATA = /^\d{4}-\d{2}-\d{2}$/;

export const qs = obj => {
  const p = Object.entries(obj).filter(([, v]) => v !== undefined && v !== null && v !== '');
  return p.length ? '?' + p.map(([k, v]) => `${k}=${encodeURIComponent(v)}`).join('&') : '';
};

export function data(v, nome) { if (v !== undefined && !DATA.test(v)) throw new Error(`${nome} deve ser AAAA-MM-DD.`); return v; }
export const reais = (v, nome) => { if (!Number.isFinite(v) || Math.round(v * 100) / 100 !== v) throw new Error(`${nome} deve ter no maximo 2 casas decimais.`); return v.toFixed(2); };
const limpo = t => String(t).replace(/[\u0000-\u001f\u007f\u2028\u2029]+/g, ' ');
export const campos = o => Object.entries(o).filter(([, v]) => v !== undefined && v !== null && v !== '').map(([k, v]) => `${k}=${JSON.stringify(limpo(v))}`).join(' ');
export function uuid(v, nome) { if (v !== undefined && !UUID.test(v)) throw new Error(`${nome} deve ser um UUID.`); return v; }

export const consultas = {
  dia: ({ data: d, loja_id }) => '/api/caixa/dia' + qs({ data: data(d, 'data'), lojaId: uuid(loja_id, 'loja_id') }),
  movimentos: ({ desde, ate, tipo, pagina = 1, por_pagina = 50 }) =>
    '/api/caixa/movimentos' + qs({ desde: data(desde, 'desde'), ate: data(ate, 'ate'), tipo, page: pagina, pageSize: Math.min(por_pagina, 100) }),
  linhas: ({ desde, ate }) => '/api/caixa/linhas' + qs({ desde: data(desde, 'desde'), ate: data(ate, 'ate') }),
  fechamentos: ({ desde, ate, pagina = 1, por_pagina = 30 }) =>
    '/api/caixa/fechamentos' + qs({ desde: data(desde, 'desde'), ate: data(ate, 'ate'), page: pagina, pageSize: Math.min(por_pagina, 100) }),
};

/** Corpos de escrita. Identidade, origem e empresa vem do token no servidor da API, nunca daqui. O resumo mostra TODOS os campos enviados. */
export const escritas = {
  abrir: a => {
    const { saldo_inicial = 0, loja_id, observacoes } = a;
    return { resumo: `ABRIR caixa: ${campos({ saldo_inicial: reais(saldo_inicial, 'saldo_inicial'), loja_id, observacoes })}`,
      caminho: '/api/caixa/abrir', corpo: { saldoInicial: saldo_inicial, lojaId: uuid(loja_id, 'loja_id'), observacoes } };
  },
  fechar: a => {
    const { data: d, valor_contado, loja_id, observacoes } = a;
    return { resumo: `FECHAR caixa: ${campos({ data: data(d, 'data') ?? '(sem data: a API fecha a sessao pendente mais recente, que pode ser de dia anterior)', valor_contado: valor_contado === undefined ? undefined : reais(valor_contado, 'valor_contado'), loja_id, observacoes })}`,
      caminho: '/api/caixa/fechar', corpo: { data: d, valorContado: valor_contado, lojaId: uuid(loja_id, 'loja_id'), observacoes } };
  },
  movimento: a => {
    const { tipo, valor, descricao, metodo, categoria, loja_id } = a;
    if (!['entrada', 'saida'].includes(tipo)) throw new Error('tipo deve ser entrada ou saida.');
    if (!(valor > 0)) throw new Error('valor deve ser maior que zero.');
    return { resumo: `LANCAR ${tipo.toUpperCase()}: ${campos({ valor: reais(valor, 'valor'), descricao, metodo, categoria, loja_id })}`,
      caminho: '/api/caixa/movimentos', corpo: { tipo, valor, descricao, metodo, categoria, lojaId: uuid(loja_id, 'loja_id') } };
  },
};

/** Confirmacao pela pessoa via elicitation do cliente. Sem suporte ou sem "confirmo: true", recusa: nunca confirma sozinho. */
export function criarPedir(srv) {
  return async mensagem => {
    if (!srv.getClientCapabilities()?.elicitation) throw new Error('este cliente nao suporta confirmacao (elicitation); escrita recusada.');
    const r = await srv.elicitInput({ message: mensagem, requestedSchema: { type: 'object', properties: { confirmo: { type: 'boolean', title: 'Confirmo esta operacao' } }, required: ['confirmo'] } });
    return r.action === 'accept' && r.content?.confirmo === true;
  };
}

/** Escrita so com BABA_CAIXA_ESCRITA=1 e confirmacao da pessoa pelo cliente (fora do canal do modelo). */
export async function escrever({ env = process.env, cliente, pedir, plano, flag = 'BABA_CAIXA_ESCRITA' }) {
  if (env[flag] !== '1') throw new Error(`escrita desligada: defina ${flag}=1 na sua maquina.`);
  const ok = await pedir(`Confirma na Casa da Baba?\n${plano.resumo}`);
  if (!ok) return { feito: false, motivo: 'nao confirmado pela pessoa' };
  const corpo = Object.fromEntries(Object.entries(plano.corpo).filter(([, v]) => v !== undefined));
  return { feito: true, resposta: await cliente.post(plano.caminho, corpo) };
}
