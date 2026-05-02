/** @type {import('tailwindcss').Config} */
module.exports = {
  content: [
    './Pages/**/*.cshtml',
    './Pages/**/*.cs',
    './wwwroot/js/**/*.js',
  ],
  // Admin é dark-first via <html data-theme="dark">.
  darkMode: ['class', '[data-theme="dark"]'],
  safelist: [
    { pattern: /^(bg|text|border)-(navy|orange|es-)-(50|100|200|400|500|600|700|800|900)$/ },
  ],
  theme: {
    extend: {
      colors: {
        // Tokens DS
        navy: {
          900: '#06143A', 800: '#0A1F52', 700: '#0E2A6E',
          600: '#15388A', 500: '#1E48A8',
          200: '#BCC8E5', 100: '#DDE4F2', 50: '#EDF1F9',
        },
        orange: {
          700: '#BF4307', 600: '#E85814', 500: '#F26B25', 400: '#F8884A',
          200: '#FAC8AB', 100: '#FCDCC8', 50: '#FEEFE3',
        },
        // Aliases legacy mantidos para compat com templates atuais
        'es-bg':        '#111827',
        'es-surface':   '#172033',
        'es-surface-2': '#202b42',
        'es-card':      '#151f31',
        'es-accent':    '#F26B25',
        'es-accent-h':  '#E85814',
        'es-basil':     '#4fbf9f',
        'es-gold':      '#f0b84a',
        'es-cream':     '#f1dcc0',
        'es-ink':       '#f7f2e8',
        'es-ink-dim':   '#c8c1b4',
        'es-ink-mute':  '#8793a7',
      },
      fontFamily: {
        sans:    ['Inter', '-apple-system', 'sans-serif'],
        display: ['Fraunces', 'serif'],
        serif:   ['Fraunces', 'serif'],
        mono:    ['"JetBrains Mono"', 'ui-monospace', 'monospace'],
      },
    },
  },
  plugins: [],
};
