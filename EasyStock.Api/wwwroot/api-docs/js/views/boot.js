// Boot screen estilo console: logo glitch + log animado de boot + CTA pulsante.
// Skipável com qualquer tecla/click. Auto-skip rápido se já visitou (cookie).

import * as store from '../store.js';

export function renderBoot(root, state) {
    const { spec } = state;
    const counts = spec?.counts || {};
    const visited = state.visited;

    root.innerHTML = `
        <section class="es-boot" data-visited="${visited}">
            <div class="es-boot-bg" aria-hidden="true"></div>
            <div class="es-boot-frame" aria-hidden="true"></div>
            <div class="es-boot-content">
                <div class="es-boot-logo">
                    <span class="es-boot-logo-text" data-text="EASYSTOCK">EASYSTOCK</span>
                    <span class="es-boot-logo-sub">// CONSOLE · v1 · OpenAPI runtime</span>
                </div>
                <pre class="es-boot-log" id="es-boot-log" aria-live="polite"></pre>
                <button class="es-boot-cta" id="es-boot-enter" type="button">
                    <span class="es-boot-cta-bracket">[</span>
                    <span>ENTER CONSOLE</span>
                    <span class="es-boot-cta-bracket">]</span>
                </button>
                <p class="es-boot-hint">enter · espaço · click · esc</p>
            </div>
        </section>
    `;

    const logEl = document.getElementById('es-boot-log');
    const ctaEl = document.getElementById('es-boot-enter');

    const lines = [
        '> es-console init',
        '> loading openapi spec ............ ok',
        `> parsed ${counts.endpoints || 0} endpoints across ${counts.tags || 0} tags`,
        `> indexed ${counts.schemas || 0} schemas / ${counts.paths || 0} paths`,
        '> module router ................... ok',
        '> fonts (fraunces, manrope, mono) . ok',
        '> ready'
    ];

    let lineIdx = 0;
    let charIdx = 0;
    let acc = '';
    const charDelay = visited ? 4 : 14;
    let timer = null;

    function tick() {
        if (lineIdx >= lines.length) {
            stopTimer();
            ctaEl.classList.add('es-pulse');
            return;
        }
        const line = lines[lineIdx];
        if (charIdx < line.length) {
            acc += line[charIdx++];
            logEl.textContent = acc;
        } else {
            acc += '\n';
            lineIdx++;
            charIdx = 0;
            logEl.textContent = acc;
        }
    }

    function stopTimer() {
        if (timer) { clearInterval(timer); timer = null; }
    }

    timer = setInterval(tick, charDelay);

    function exit() {
        stopTimer();
        window.removeEventListener('keydown', keyHandler);
        store.set({ booting: false, visited: true });
    }

    function keyHandler(e) {
        if (['Enter', ' ', 'Escape'].includes(e.key)) {
            e.preventDefault();
            exit();
        }
    }

    ctaEl.addEventListener('click', exit);
    window.addEventListener('keydown', keyHandler);

    // Auto-skip rápido se já visitou
    if (visited) {
        setTimeout(exit, 700);
    }
}
