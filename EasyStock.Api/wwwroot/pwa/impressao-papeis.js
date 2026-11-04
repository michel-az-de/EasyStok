(function (global) {
  'use strict';
  // #2202 (impressão v2, F0): catálogo único de papéis. Ajustes, resolvedor e
  // gerador TSPL leem daqui; nenhum outro lugar define tamanho de etiqueta.
  //
  // tipo: 'continuo' (rolo sem recorte, D1: padrão), 'etiqueta' (recortada,
  // com gap entre uma e outra) ou 'bobina' (cupom ESC/POS).
  // classe: o documento que o papel serve ('etiqueta' ou 'cupom').
  // compr: comprimento nominal do desenho no rolo contínuo, por orientação.
  // O desenho encolhe até o conteúdo; o comprimento é só o teto.
  const PAPEIS = [
    { id: 'continuo-52', classe: 'etiqueta', tipo: 'continuo', larguraMm: 52, compr: { vertical: 75, horizontal: 75 }, rotulo: 'Rolo contínuo de 52 mm' },
    { id: 'continuo-80', classe: 'etiqueta', tipo: 'continuo', larguraMm: 80, compr: { vertical: 100, horizontal: 50 }, rotulo: 'Rolo contínuo de 80 mm' },
    { id: 'continuo-100', classe: 'etiqueta', tipo: 'continuo', larguraMm: 100, compr: { vertical: 150, horizontal: 150 }, rotulo: 'Rolo contínuo de 100 mm (10x15)' },
    { id: 'etiqueta-40x25', classe: 'etiqueta', tipo: 'etiqueta', larguraMm: 40, alturaMm: 25, gapMm: 2, rotulo: 'Etiqueta 40 x 25 mm' },
    { id: 'etiqueta-50x30', classe: 'etiqueta', tipo: 'etiqueta', larguraMm: 50, alturaMm: 30, gapMm: 2, rotulo: 'Etiqueta 50 x 30 mm' },
    { id: 'etiqueta-52x30', classe: 'etiqueta', tipo: 'etiqueta', larguraMm: 52, alturaMm: 30, gapMm: 2, rotulo: 'Etiqueta 52 x 30 mm' },
    { id: 'etiqueta-52x80', classe: 'etiqueta', tipo: 'etiqueta', larguraMm: 52, alturaMm: 80, gapMm: 2, rotulo: 'Etiqueta 52 x 80 mm' },
    { id: 'etiqueta-60x40', classe: 'etiqueta', tipo: 'etiqueta', larguraMm: 60, alturaMm: 40, gapMm: 2, rotulo: 'Etiqueta 60 x 40 mm' },
    { id: 'etiqueta-80x40', classe: 'etiqueta', tipo: 'etiqueta', larguraMm: 80, alturaMm: 40, gapMm: 2, rotulo: 'Etiqueta 80 x 40 mm' },
    { id: 'etiqueta-80x50', classe: 'etiqueta', tipo: 'etiqueta', larguraMm: 80, alturaMm: 50, gapMm: 2, rotulo: 'Etiqueta 80 x 50 mm' },
    { id: 'etiqueta-100x50', classe: 'etiqueta', tipo: 'etiqueta', larguraMm: 100, alturaMm: 50, gapMm: 2, rotulo: 'Etiqueta 100 x 50 mm' },
    { id: 'etiqueta-100x100', classe: 'etiqueta', tipo: 'etiqueta', larguraMm: 100, alturaMm: 100, gapMm: 3, rotulo: 'Etiqueta 100 x 100 mm' },
    { id: 'etiqueta-100x150', classe: 'etiqueta', tipo: 'etiqueta', larguraMm: 100, alturaMm: 150, gapMm: 3, rotulo: 'Etiqueta 100 x 150 mm (10x15)' },
    { id: 'bobina-58', classe: 'cupom', tipo: 'bobina', larguraMm: 58, utilMm: 48, rotulo: 'Bobina de 58 mm' },
    { id: 'bobina-80', classe: 'cupom', tipo: 'bobina', larguraMm: 80, utilMm: 72, rotulo: 'Bobina de 80 mm' }
  ];
  const PADRAO_TSPL = 'continuo-80';
  const PADRAO_ESCPOS = 'bobina-58';
  const CABECA_MM = 104; // Oasis OIA-8381-B, 203 dpi

  function copia(p) { return p ? JSON.parse(JSON.stringify(p)) : null; }
  function porId(id) { return copia(PAPEIS.find(p => p.id === id)); }
  function lista(filtro) { return PAPEIS.filter(p => !filtro || Object.keys(filtro).every(k => p[k] === filtro[k])).map(copia); }

  function positivo(v) { const n = Number(v); return Number.isFinite(n) && n > 0 ? n : 0; }

  // Mídia gravada na impressora -> papel do catálogo, ou um papel
  // personalizado com as medidas que o operador informou.
  function daMidia(m) {
    if (!m) return null;
    if (m.preset) { const p = porId(m.preset); if (p) return p; }
    const tipo = m.tipo === 'recortado' ? 'etiqueta' : m.tipo;
    const L = positivo(m.larguraMm);
    if (!L) return null;
    if (tipo === 'continuo') {
      const p = PAPEIS.find(x => x.tipo === 'continuo' && x.larguraMm === L);
      return p ? copia(p) : { id: 'continuo-' + L, classe: 'etiqueta', tipo: 'continuo', larguraMm: L, compr: { vertical: Math.round(L * 1.4), horizontal: Math.round(L * 1.4) }, rotulo: 'Rolo contínuo de ' + L + ' mm' };
    }
    if (tipo === 'bobina') {
      const p = PAPEIS.find(x => x.tipo === 'bobina' && x.larguraMm === L);
      return p ? copia(p) : { id: 'bobina-' + L, classe: 'cupom', tipo: 'bobina', larguraMm: L, utilMm: L - 10, rotulo: 'Bobina de ' + L + ' mm' };
    }
    const A = positivo(m.alturaMm);
    if (!A) return null;
    const gap = m.gapMm === 0 ? 0 : (positivo(m.gapMm) || 2);
    const p = PAPEIS.find(x => x.tipo === 'etiqueta' && x.larguraMm === L && x.alturaMm === A);
    return p ? Object.assign(copia(p), { gapMm: gap }) : { id: 'etiqueta-' + L + 'x' + A, classe: 'etiqueta', tipo: 'etiqueta', larguraMm: L, alturaMm: A, gapMm: gap, rotulo: 'Etiqueta ' + L + ' x ' + A + ' mm' };
  }

  // Papel -> mídia gravada na impressora (o que vai para o cdb-impressoras.v2).
  function paraMidia(papel) {
    const p = typeof papel === 'string' ? porId(papel) : papel;
    if (!p) return null;
    const m = { preset: p.id, tipo: p.tipo, larguraMm: p.larguraMm };
    if (p.tipo === 'etiqueta') { m.alturaMm = p.alturaMm; m.gapMm = p.gapMm; }
    return m;
  }

  // Tamanho do desenho no papel, em mm, para a orientação pedida. A vertical
  // fica em pé (largura menor), a horizontal deitada. Se o desenho não tem a
  // largura do papel, ele sai girado 90° (o rolo só avança num sentido).
  function area(papel, orientacao) {
    const p = typeof papel === 'string' ? porId(papel) : papel;
    if (!p) return null;
    const o = orientacao === 'horizontal' ? 'horizontal' : 'vertical';
    const largura = p.tipo === 'bobina' ? p.utilMm : p.larguraMm;
    const comprimento = p.tipo === 'etiqueta' ? p.alturaMm : p.tipo === 'continuo' ? p.compr[o] : 0;
    if (!comprimento) return { w_mm: largura, h_mm: 0, girar: false, continuo: true, papel: p };
    const menor = Math.min(largura, comprimento), maior = Math.max(largura, comprimento);
    const w = o === 'horizontal' ? maior : menor;
    const h = o === 'horizontal' ? menor : maior;
    return { w_mm: w, h_mm: h, girar: w !== largura, continuo: p.tipo === 'continuo', papel: p };
  }

  function descrever(papel) {
    const p = typeof papel === 'string' ? porId(papel) : papel;
    return p ? p.rotulo : 'Papel não informado';
  }

  global.cdbPapeis = { PAPEIS: lista(), PADRAO_TSPL, PADRAO_ESCPOS, CABECA_MM, porId, lista, daMidia, paraMidia, area, descrever };
})(typeof window !== 'undefined' ? window : globalThis);
