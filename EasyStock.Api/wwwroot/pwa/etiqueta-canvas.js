(function (global) {
  'use strict';
  // #2202 (impressão v2, F2): backend canvas do LayoutJson v1 para térmica de
  // 203 dpi. Desenha no tamanho final (8 pontos/mm), gira 90° quando o
  // desenho não tem a largura do papel e entrega TSPL (Oasis) ou GS v 0
  // (ESC/POS de 80 mm). A bobina de 58 mm continua no etiqueta-termica.js.
  const PONTOS_MM = 8;
  const MARGEM_ROLO_MM = 1.5;
  const BRANCO = '#fff';
  const PRETO = '#000';

  function R() { return global.cdbResolverEtiqueta; }
  function px(mm) { return Math.round(mm * PONTOS_MM); }

  // Code128 C: pares de dígitos. A tabela de padrões é a do app (index.html).
  function code128C(digitos, padroes) {
    const tabela = padroes || (typeof CODE128_PATTERNS !== 'undefined' ? CODE128_PATTERNS : null); // eslint-disable-line no-undef
    if (!tabela) throw new Error('tabela Code128 indisponível');
    const v = [105];
    for (let i = 0; i < digitos.length; i += 2) v.push(Number(digitos.slice(i, i + 2)));
    let soma = v[0];
    for (let i = 1; i < v.length; i++) soma += v[i] * i;
    v.push(soma % 103, 106);
    return v.map(x => tabela[x]).join('');
  }

  function novoCanvas(doc, w, h) {
    const c = doc.createElement('canvas');
    c.width = w;
    c.height = h;
    return c;
  }

  // LayoutJson -> canvas binário, em pontos. Mesmo limiar do dadosBitmap.
  function rasterizar(layout, opts) {
    const o = opts || {};
    const doc = o.document || global.document;
    const W = px(layout.size.w_mm), H = px(layout.size.h_mm);
    const c = novoCanvas(doc, W, H);
    const x = c.getContext('2d');
    x.fillStyle = BRANCO;
    x.fillRect(0, 0, W, H);
    x.fillStyle = PRETO;
    for (const e of layout.elements) {
      const bx = px(e.x_mm), by = px(e.y_mm), bw = px(e.w_mm), bh = px(e.h_mm);
      if (e.type === 'text') {
        x.save();
        x.beginPath();
        x.rect(bx, by, bw, bh);
        x.clip();
        x.font = `${e.weight} ${Math.round(e.size_pt * R().PT_MM * PONTOS_MM * 100) / 100}px ${R().FONTE}`;
        x.textBaseline = 'middle';
        x.textAlign = e.align === 'center' ? 'center' : 'left';
        x.fillText(e.content, e.align === 'center' ? bx + bw / 2 : bx, by + bh / 2 + 1);
        x.restore();
      } else if (e.type === 'code' && e.format === 'qr') {
        const q = global.qrcode(0, R().QR_NIVEL || 'Q');
        q.addData(e.content);
        q.make();
        const n = q.getModuleCount();
        const cel = Math.max(1, Math.round((e._mod || e.w_mm / (n + 2)) * PONTOS_MM));
        const ox = bx + cel, oy = by + cel;
        for (let r = 0; r < n; r++) for (let k = 0; k < n; k++) if (q.isDark(r, k)) x.fillRect(ox + k * cel, oy + r * cel, cel, cel);
      } else if (e.type === 'code' && e.format === 'barcode-code128') {
        const mods = code128C(e.content, o.padroes128);
        const mw = Math.max(1, Math.round(e._mod * PONTOS_MM));
        let cx = bx + Math.round(e.quiet_zone_mm * PONTOS_MM);
        for (let i = 0; i < mods.length; i++) {
          const w = Number(mods[i]) * mw;
          if (i % 2 === 0) x.fillRect(cx, by, w, bh);
          cx += w;
        }
      }
    }
    const img = x.getImageData(0, 0, W, H);
    const d = img.data;
    for (let i = 0; i < d.length; i += 4) {
      const v = d[i + 3] >= 128 && (d[i] * 299 + d[i + 1] * 587 + d[i + 2] * 114) / 1000 < 128 ? 0 : 255;
      d[i] = d[i + 1] = d[i + 2] = v;
      d[i + 3] = 255;
    }
    x.putImageData(img, 0, 0);
    return c;
  }

  // 90° no sentido horário: o topo do desenho vai para a direita do papel.
  function girar90(c, doc) {
    const r = novoCanvas(doc || global.document, c.height, c.width);
    const x = r.getContext('2d');
    x.translate(r.width, 0);
    x.rotate(Math.PI / 2);
    x.drawImage(c, 0, 0);
    return r;
  }

  // Rolo contínuo: corta as linhas brancas de cima e de baixo. Fica 1,5 mm
  // de margem em volta da tinta ou a zona quieta dos códigos, o que for
  // maior (a zona quieta já é papel em branco). Altura em mm inteiro.
  function aparar(c, protegido, doc) {
    const W = c.width, H = c.height;
    const d = c.getContext('2d').getImageData(0, 0, W, H).data;
    let topo = -1, base = -1;
    for (let y = 0; y < H && topo < 0; y++) for (let k = 0; k < W; k++) if (d[(y * W + k) * 4] < 128) { topo = y; break; }
    for (let y = H - 1; y >= 0 && base < 0; y--) for (let k = 0; k < W; k++) if (d[(y * W + k) * 4] < 128) { base = y; break; }
    if (topo < 0) return c;
    const margem = px(MARGEM_ROLO_MM);
    topo -= margem;
    base += margem;
    if (protegido) { topo = Math.min(topo, protegido[0]); base = Math.max(base, protegido[1]); }
    topo = Math.max(0, topo);
    base = Math.min(H - 1, base);
    const altura = Math.ceil((base - topo + 1) / PONTOS_MM) * PONTOS_MM;
    const out = novoCanvas(doc || global.document, W, altura);
    const x = out.getContext('2d');
    x.fillStyle = BRANCO;
    x.fillRect(0, 0, W, altura);
    x.drawImage(c, 0, topo, W, Math.min(altura, H - topo), 0, 0, W, Math.min(altura, H - topo));
    return out;
  }

  // Área do desenho para o papel e a orientação. A bobina não tem
  // comprimento: o teto é proporcional à largura útil.
  function areaDoPapel(papel, orientacao) {
    const a = global.cdbPapeis.area(papel, orientacao);
    if (!a) return null;
    if (a.h_mm) return a;
    const h = orientacao === 'horizontal' ? Math.round(a.w_mm * 0.62) : Math.round(a.w_mm * 1.4);
    return Object.assign({}, a, { h_mm: h, continuo: true, girar: false });
  }

  // Resolve no papel na orientação pedida; se não cabe, tenta a outra e avisa.
  // Nas duas sem caber, a recusa diz o que fazer. Devolve o resultado do
  // resolver com { orientacao, area } da tentativa usada.
  function resolverNoPapel(campos, papel, orientacao, opcoes) {
    const tentar = ori => {
      const area = areaDoPapel(papel, ori);
      return Object.assign(R().resolver(campos || {}, area, Object.assign({}, opcoes, { orientacao: ori })), { orientacao: ori, area });
    };
    const r = tentar(orientacao);
    if (!r.recusa) return r;
    const outra = orientacao === 'horizontal' ? 'vertical' : 'horizontal';
    const r2 = tentar(outra);
    if (!r2.recusa) {
      r2.avisos.push(outra === 'horizontal' ? 'saiu deitada: em pé não cabe' : 'saiu em pé: deitada não cabe');
      return r2;
    }
    r.recusa += '. Escolha um papel maior em Ajustes';
    return r;
  }

  // entrada: { campos, papel, orientacao, codigos, document?, medir?, padroes128? }
  // Devolve { canvas, layout, recusa, avisos, omitidos, papel, continuo, orientacao }.
  function desenhar(entrada) {
    const e = entrada || {};
    const doc = e.document || global.document;
    const papel = typeof e.papel === 'string' ? global.cdbPapeis.porId(e.papel) : e.papel;
    if (!papel) return { canvas: null, layout: null, recusa: 'papel não informado', avisos: [], omitidos: [] };
    const r = resolverNoPapel(e.campos, papel, e.orientacao, { codigos: e.codigos || ['qr'], medir: e.medir, document: doc });
    const area = r.area;
    if (r.recusa) return { canvas: null, layout: null, recusa: r.recusa, avisos: r.avisos, omitidos: r.omitidos, papel, continuo: area.continuo, orientacao: r.orientacao };
    let c = rasterizar(r.layout, { document: doc, padroes128: e.padroes128 });
    // Caixas dos códigos com a zona quieta: o aparar não pode comê-las.
    const caixas = [];
    if (r.qrCaixa) { const q = r.qrCaixa; caixas.push({ x0: q.x_mm, x1: q.x_mm + q.lado_mm, y0: q.y_mm, y1: q.y_mm + q.lado_mm }); }
    for (const el of r.layout.elements) if (el.format === 'barcode-code128') caixas.push({ x0: el.x_mm, x1: el.x_mm + el.w_mm, y0: el.y_mm, y1: el.y_mm + el.h_mm });
    let protegido = null;
    if (caixas.length) {
      const ini = Math.min.apply(null, caixas.map(b => (area.girar ? b.x0 : b.y0)));
      const fim = Math.max.apply(null, caixas.map(b => (area.girar ? b.x1 : b.y1)));
      protegido = [px(ini), px(fim) - 1];
    }
    if (area.girar) c = girar90(c, doc);
    if (area.continuo) c = aparar(c, protegido, doc);
    return { canvas: c, layout: r.layout, recusa: null, avisos: r.avisos, omitidos: r.omitidos, papel, continuo: area.continuo, orientacao: r.orientacao };
  }

  function rgba(c) { return c.getContext('2d').getImageData(0, 0, c.width, c.height).data; }

  // TSPL na largura do papel, BITMAP na coluna 0. Contínuo: SIZE com a altura
  // do conteúdo e GAP 0,0. Recortado: SIZE da etiqueta, gap e SET TEAR ON.
  function tspl(entrada) {
    const e = entrada || {};
    const r = desenhar(e);
    if (r.recusa) return Object.assign(r, { bytes: null });
    const imp = e.impressora || {};
    const off = imp.offsetMm || {};
    const c = r.canvas;
    const bytes = global.cdbTspl.etiqueta(rgba(c), c.width, c.height, {
      larguraMm: r.papel.larguraMm,
      alturaMm: r.continuo ? c.height / PONTOS_MM : r.papel.alturaMm,
      continuo: r.continuo,
      gapMm: r.continuo ? 0 : r.papel.gapMm,
      densidade: imp.densidade,
      velocidade: imp.velocidade,
      inverter: !!imp.inverter,
      copias: e.copias || 1,
      x: Math.max(0, px(Number(off.x) || 0)),
      y: Math.max(0, px(Number(off.y) || 0))
    });
    return Object.assign(r, { bytes });
  }

  // ESC/POS (bobina de 80 mm): GS v 0 na largura útil, avanço e sem corte.
  function escpos(entrada) {
    const e = entrada || {};
    const r = desenhar(e);
    if (r.recusa) return Object.assign(r, { bytes: null });
    const c = r.canvas;
    const corpo = global.cdbEtiquetaTermica.raster(rgba(c), c.width, c.height);
    const copias = Math.max(1, Math.floor(Number(e.copias) || 1));
    const uma = new Uint8Array(2 + corpo.length + 3);
    uma.set([0x1B, 0x40], 0);
    uma.set(corpo, 2);
    uma.set([0x1B, 0x64, 0x04], 2 + corpo.length);
    const out = new Uint8Array(uma.length * copias);
    for (let i = 0; i < copias; i++) out.set(uma, i * uma.length);
    return Object.assign(r, { bytes: out });
  }

  global.cdbEtiquetaCanvas = { PONTOS_MM, rasterizar, girar90, aparar, areaDoPapel, resolverNoPapel, desenhar, tspl, escpos, code128C };
})(typeof window !== 'undefined' ? window : globalThis);
