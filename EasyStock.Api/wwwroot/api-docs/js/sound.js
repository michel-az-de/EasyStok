// SFX via Web Audio API — gera tons inline, zero asset.
// Respeita toggle store.soundOn. Lazy AudioContext (cria na primeira chamada — Chrome bloqueia ctx pré-gesto).

import * as store from './store.js';

let ctx = null;

function getCtx() {
    if (!ctx) {
        try {
            const Ctor = window.AudioContext || window.webkitAudioContext;
            if (!Ctor) return null;
            ctx = new Ctor();
        } catch { return null; }
    }
    if (ctx.state === 'suspended') {
        try { ctx.resume(); } catch {}
    }
    return ctx;
}

function playTone(c, freq, dur, type, delay = 0, peakGain = 0.07, slideTo = null) {
    const osc = c.createOscillator();
    const gain = c.createGain();
    osc.connect(gain);
    gain.connect(c.destination);
    const start = c.currentTime + delay;
    osc.type = type;
    osc.frequency.setValueAtTime(freq, start);
    if (slideTo != null) {
        osc.frequency.exponentialRampToValueAtTime(Math.max(40, slideTo), start + dur);
    }
    gain.gain.setValueAtTime(0, start);
    gain.gain.linearRampToValueAtTime(peakGain, start + 0.005);
    gain.gain.exponentialRampToValueAtTime(0.0001, start + dur);
    osc.start(start);
    osc.stop(start + dur + 0.02);
}

export function play(kind) {
    if (!store.get().soundOn) return;
    const c = getCtx();
    if (!c) return;
    switch (kind) {
        case 'click':
            playTone(c, 880, 0.04, 'square', 0, 0.04, 440);
            break;
        case 'hover':
            playTone(c, 520, 0.025, 'triangle', 0, 0.025, 300);
            break;
        case 'fire':
            playTone(c, 180, 0.18, 'sawtooth', 0, 0.10, 880);
            playTone(c, 90, 0.20, 'square', 0.02, 0.05, 60);
            break;
        case 'success':
            playTone(c, 660, 0.08, 'sine', 0, 0.06);
            playTone(c, 880, 0.10, 'sine', 0.06, 0.06);
            playTone(c, 1100, 0.14, 'sine', 0.13, 0.05);
            break;
        case 'error':
            playTone(c, 220, 0.10, 'sawtooth', 0, 0.06, 110);
            playTone(c, 165, 0.14, 'sawtooth', 0.07, 0.05, 80);
            break;
        case 'open':
            playTone(c, 440, 0.06, 'sine', 0, 0.05, 660);
            break;
        case 'close':
            playTone(c, 660, 0.06, 'sine', 0, 0.05, 440);
            break;
        default:
            playTone(c, 600, 0.04, 'triangle', 0, 0.04);
    }
}

// Resume context numa primeira interação (caso o browser tenha bloqueado).
let unlocked = false;
export function unlock() {
    if (unlocked) return;
    const c = getCtx();
    if (c) unlocked = true;
}
