// Redacao local: segunda camada, aplicada a todo texto vindo da VPS antes de ir ao contexto do modelo.
const REGRAS = [
  [/Bearer\s+[A-Za-z0-9\-._~+/]+=*/gi, 'Bearer [REDIGIDO]'],
  [/eyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]*/g, '[JWT-REDIGIDO]'],
  [/((?:password|pwd|senha|secret|apikey|api[_-]?key|token|access[_-]?token|refresh[_-]?token|client[_-]?secret|authorization)\s*["']?\s*[=:]\s*["']?)[^\s;,"'&]+/gi, '$1[REDIGIDO]'],
  [/(\b(?:Host|Server|Data Source)=[^;\s]+;[^\n]*)/gi, '[CONNECTION-STRING-REDIGIDA]'],
  [/\b(postgres(?:ql)?|mysql|redis|amqp|mongodb):\/\/[^\s"']+/gi, '$1://[REDIGIDO]'],
  [/[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}/g, '[EMAIL]'],
  [/\b\d{3}\.?\d{3}\.?\d{3}-?\d{2}\b/g, '[CPF]'],
  [/(?<![\d])(?:\+?55\s?)?\(?\d{2}\)?\s?9?\d{4}-?\d{4}(?![\d])/g, '[TELEFONE]'],
];

export function redigir(texto) {
  let t = String(texto ?? '');
  for (const [re, sub] of REGRAS) t = t.replace(re, sub);
  return t;
}
