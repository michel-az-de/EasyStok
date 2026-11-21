// Redacao local: segunda camada, aplicada a todo texto vindo da VPS antes de ir ao contexto do modelo.
// LGPD (ADR-0069): chaves de nome, endereco e CEP. A chave composta nomeProduto nao casa; "Nome" solto e redigido.
const PESSOAL = String.raw`\b(?:nome(?:[_-]?(?:cliente|completo|destinatario|contato|pessoa|usuario))?|cliente(?:[_-]?nome)?|destinatario|endere(?:c|ç)o\w*|logradouro|bairro|complemento|cep)\\?["']?\s*[=:]\s*`;
const SEGREDO = String.raw`(?:password|pwd|senha|secret|apikey|api[_-]?key|token|access[_-]?token|refresh[_-]?token|client[_-]?secret|authorization)`;
const REGRAS = [
  [/Bearer\s+[A-Za-z0-9\-._~+/]+=*/gi, 'Bearer [REDIGIDO]'],
  [/eyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]*/g, '[JWT-REDIGIDO]'],
  // Authorization com outro esquema (Basic, Digest...): a credencial vem depois do nome do esquema (#3078)
  [/(authorization\s*["']?\s*[=:]\s*["']?\s*)(?:Basic|Digest|Negotiate|NTLM)\s+[^\s;,"'&]+/gi, '$1[REDIGIDO]'],
  // segredo entre aspas: tudo ate a aspa que fecha, espacos incluidos (#3078)
  [new RegExp(String.raw`(${SEGREDO}\\?["']?\s*[=:]\s*)(\\?["'])(?:(?!\2)[^\n])*\2`, 'gi'), '$1$2[REDIGIDO]$2'],
  [new RegExp(String.raw`(${SEGREDO}\s*["']?\s*[=:]\s*["']?)[^\s;,"'&]+`, 'gi'), '$1[REDIGIDO]'],
  [/(\b(?:Host|Server|Data Source)=[^;\s]+;[^\n]*)/gi, '[CONNECTION-STRING-REDIGIDA]'],
  [/\b(postgres(?:ql)?|mysql|redis|amqp|mongodb):\/\/[^\s"']+/gi, '$1://[REDIGIDO]'],
  [/((?:set-)?cookie\s*:)[^\n]*/gi, '$1 [REDIGIDO]'],
  [/([?&](?:key|sig|signature|code|token|apikey)=)[^&\s"']+/gi, '$1[REDIGIDO]'],
  [/[A-Za-z0-9._%+-]{1,64}@[A-Za-z0-9.-]{1,255}\.[A-Za-z]{2,24}/g, '[EMAIL]'],
  [/\b\d{3}\.?\d{3}\.?\d{3}-?\d{2}\b/g, '[CPF]'],
  [/(?<![\d])(?:\+?55\s?)?\(?\d{2}\)?\s?9?\d{4}-?\d{4}(?![\d])/g, '[TELEFONE]'],
  // valor entre aspas (inclusive JSON escapado dentro do log): tudo ate a aspa que fecha, virgulas incluidas
  [new RegExp(String.raw`(${PESSOAL})(\\?["'])((?:(?!\2)[^\n])*)\2`, 'gi'), '$1$2[REDIGIDO]$2'],
  // valor sem aspas: ate o separador ou a proxima chave=valor, para o resto da linha continuar legivel
  [new RegExp(String.raw`(${PESSOAL})(?![\\"'\s])(?:(?!\s+[\w.-]+\s*[=:])[^"'\n,;|&{}[\]\\])+`, 'gi'), '$1[REDIGIDO]'],
  [/\b\d{5}-\d{3}\b/g, '[CEP]'],
];

const MAX_LINHA = 4096;

/** Corta linhas gigantes antes das regex (evita custo quadratico) e redige. */
export function redigir(texto) {
  let t = String(texto ?? '')
    .split('\n')
    .map(l => (l.length > MAX_LINHA ? l.slice(0, MAX_LINHA) + ' [LINHA-CORTADA]' : l))
    .join('\n');
  for (const [re, sub] of REGRAS) t = t.replace(re, sub);
  return t;
}
