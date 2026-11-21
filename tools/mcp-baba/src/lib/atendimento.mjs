// Regras do baba-atendimento: so consultas (leitura) pela API do atendimento. Sem escrita, sem dossie, sem midia, sem telefone de equipe.
import { qs, data, uuid } from './caixa.mjs';

const B = '/api/atendimento';
const PALAVRA = /^[A-Za-z]{1,40}$/;
const MOMENTO = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2}(\.\d+)?)?(Z|[+-]\d{2}:\d{2})?$/;

function palavra(v, nome) { if (v !== undefined && !PALAVRA.test(v)) throw new Error(`${nome} invalido.`); return v; }
function id(v, nome) { if (!v) throw new Error(`${nome} e obrigatorio.`); return uuid(v, nome); }
function inteiro(v, nome, max) { if (v === undefined) return v; if (!Number.isInteger(v) || v < 1 || v > max) throw new Error(`${nome} deve ser inteiro de 1 a ${max}.`); return v; }
function busca(v) { if (v === undefined) return v; if (String(v).length > 80) throw new Error('q deve ter ate 80 caracteres.'); return String(v).replace(/[\u0000-\u001f\u007f]+/g, ' '); }
function dono(v) { if (v === undefined || v === 'eu' || v === 'ninguem') return v; return uuid(v, 'responsavel'); }
function momento(v, nome) { if (v !== undefined && !MOMENTO.test(v)) throw new Error(`${nome} deve ser AAAA-MM-DDTHH:MM[:SS][Z].`); return v; }
function obrigatoria(v, nome) { if (!v) throw new Error(`${nome} e obrigatoria.`); return data(v, nome); }

export const consultas = {
  conversas: a => `${B}/conversas` + qs({ situacao: palavra(a.situacao, 'situacao'), q: busca(a.q), responsavel: dono(a.responsavel), pagina: inteiro(a.pagina, 'pagina', 10000), limite: inteiro(a.limite, 'limite', 100) }),
  mensagens: a => `${B}/conversas/${id(a.conversa_id, 'conversa_id')}/mensagens` + qs({ antesDe: momento(a.antes_de, 'antes_de'), limite: inteiro(a.limite, 'limite', 100) }),
  nao_entregues: a => `${B}/conversas/nao-entregues` + qs({ limite: inteiro(a.limite, 'limite', 100) }),
  assistente_turno: a => `${B}/conversas/${id(a.conversa_id, 'conversa_id')}/assistente`,
  assistente_historico: a => `${B}/assistente/historico` + qs({ conversaId: a.conversa_id === undefined ? undefined : uuid(a.conversa_id, 'conversa_id'), resultado: palavra(a.resultado, 'resultado'), pagina: inteiro(a.pagina, 'pagina', 10000), tamanhoPagina: inteiro(a.tamanho_pagina, 'tamanho_pagina', 100) }),
  assistente_uso: a => `${B}/assistente/uso` + qs({ ano: inteiro(a.ano, 'ano', 2100), mes: inteiro(a.mes, 'mes', 12) }),
  expediente: () => `${B}/expediente`,
  viagens: a => `${B}/viagens` + qs({ situacao: palavra(a.situacao, 'situacao') }),
  viagem: a => `${B}/viagens/${id(a.viagem_id, 'viagem_id')}`,
  agenda: a => `${B}/agenda` + qs({ de: obrigatoria(a.de, 'de'), ate: obrigatoria(a.ate, 'ate') }),
};
