// Catch the Ball: a small canvas game. Everything runs in the browser; best scores stay on this device.
(() => {
  const MUTE_KEY = 'catchBall.muted';
  const THEME_KEY = 'catchBall.theme';
  const DIFF_KEY = 'catchBall.difficulty';
  const bestKey = k => 'catchBall.best.' + k;
  const TAU = Math.PI * 2;

  const $ = id => document.getElementById(id);
  const cvs = $('game');
  const ctx = cvs.getContext('2d');
  const reduceMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  const clamp = (v, a, b) => Math.max(a, Math.min(b, v));
  const rand = (a, b) => a + Math.random() * (b - a);
  function store(key, val) {
    try {
      if (val === undefined) return localStorage.getItem(key);
      localStorage.setItem(key, String(val));
    } catch (e) { return null; }
  }

  const DIFFS = {
    easy: { label: 'Easy', speed: 0.8, spawn: 1.25, bad: 0.7, lives: 5, note: '5 hearts, slower balls, fewer hazards.' },
    normal: { label: 'Normal', speed: 1, spawn: 1, bad: 1, lives: 3, note: '3 hearts. The standard game.' },
    hard: { label: 'Hard', speed: 1.2, spawn: 0.8, bad: 1.35, lives: 3, note: '3 hearts, faster balls, more hazards.' }
  };
  const POWERS = {
    wide: { name: 'Wide basket', short: 'Wide', color: '#ff9f1c', dur: 8 },
    slow: { name: 'Slow motion', short: 'Slow', color: '#8e6cff', dur: 6 },
    magnet: { name: 'Magnet', short: 'Magnet', color: '#ff4d6d', dur: 8 },
    shield: { name: 'Shield', short: 'Shield', color: '#1fb1d1', dur: 15 }
  };

  // ---------- Shared shapes ----------
  function circle(g, x, y, r, fill, stroke, lw) {
    g.beginPath(); g.arc(x, y, r, 0, TAU);
    if (fill) { g.fillStyle = fill; g.fill(); }
    if (stroke) { g.lineWidth = lw || Math.max(1.5, r * 0.08); g.strokeStyle = stroke; g.stroke(); }
  }
  function shine(g, x, y, r) { circle(g, x - r * 0.35, y - r * 0.38, r * 0.2, 'rgba(255,255,255,0.55)'); }
  function starPath(g, x, y, outer, inner, points, rot) {
    g.beginPath();
    for (let i = 0; i < points * 2; i++) {
      const rr = i % 2 ? inner : outer, a = i * Math.PI / points - Math.PI / 2 + (rot || 0);
      g.lineTo(x + Math.cos(a) * rr, y + Math.sin(a) * rr);
    }
    g.closePath();
  }
  function goldBall(g, x, y, r, rot, ink, glow) {
    if (glow) { g.save(); g.shadowColor = '#ffd23f'; g.shadowBlur = r; circle(g, x, y, r, '#ffc21a'); g.restore(); }
    circle(g, x, y, r, '#ffc21a', ink);
    starPath(g, x, y, r * 0.52, r * 0.22, 5, rot * 0.5);
    g.fillStyle = '#ffffff'; g.fill();
  }
  function spiky(g, x, y, r, rot, body, spike, ink) {
    g.save(); g.translate(x, y); g.rotate(rot);
    g.strokeStyle = spike; g.lineCap = 'round'; g.lineWidth = Math.max(2, r * 0.14);
    for (let i = 0; i < 12; i++) {
      const a = i * TAU / 12;
      g.beginPath(); g.moveTo(Math.cos(a) * r * 0.5, Math.sin(a) * r * 0.5);
      g.lineTo(Math.cos(a) * r * 1.2, Math.sin(a) * r * 1.2); g.stroke();
    }
    g.restore();
    circle(g, x, y, r * 0.75, body, ink);
    shine(g, x, y, r * 0.75);
  }
  function drawPower(g, x, y, r, type, t) {
    const p = POWERS[type];
    const pulse = reduceMotion ? 0.5 : (Math.sin((t || 0) * 6) + 1) / 2;
    g.save();
    g.globalAlpha = 0.25 + pulse * 0.25;
    circle(g, x, y, r * (1.25 + pulse * 0.15), p.color);
    g.restore();
    circle(g, x, y, r, p.color, '#ffffff', Math.max(2, r * 0.14));
    g.save(); g.translate(x, y);
    g.strokeStyle = '#ffffff'; g.fillStyle = '#ffffff'; g.lineCap = 'round'; g.lineJoin = 'round';
    g.lineWidth = Math.max(2, r * 0.15);
    const s = r * 0.5;
    if (type === 'wide') {
      g.beginPath(); g.moveTo(-s, 0); g.lineTo(s, 0);
      g.moveTo(-s + s * 0.4, -s * 0.4); g.lineTo(-s, 0); g.lineTo(-s + s * 0.4, s * 0.4);
      g.moveTo(s - s * 0.4, -s * 0.4); g.lineTo(s, 0); g.lineTo(s - s * 0.4, s * 0.4);
      g.stroke();
    } else if (type === 'slow') {
      g.beginPath(); g.arc(0, 0, s, 0, TAU); g.stroke();
      g.beginPath(); g.moveTo(0, 0); g.lineTo(0, -s * 0.65); g.moveTo(0, 0); g.lineTo(s * 0.45, s * 0.2); g.stroke();
    } else if (type === 'magnet') {
      g.lineWidth = Math.max(3, r * 0.24); g.lineCap = 'butt';
      g.beginPath(); g.moveTo(-s * 0.7, -s * 0.8); g.lineTo(-s * 0.7, 0); g.arc(0, 0, s * 0.7, Math.PI, 0, true); g.lineTo(s * 0.7, -s * 0.8); g.stroke();
    } else if (type === 'shield') {
      g.beginPath();
      g.moveTo(0, -s); g.lineTo(s * 0.85, -s * 0.6); g.lineTo(s * 0.7, s * 0.3);
      g.quadraticCurveTo(s * 0.4, s * 0.8, 0, s); g.quadraticCurveTo(-s * 0.4, s * 0.8, -s * 0.7, s * 0.3);
      g.lineTo(-s * 0.85, -s * 0.6); g.closePath(); g.fill();
    }
    g.restore();
  }
  function bucket(g, x, top, w, h, tilt, squash, s, inner) {
    g.save();
    g.fillStyle = s.shadow;
    g.beginPath(); g.ellipse(x, top + h + 4, w * 0.46, h * 0.09, 0, 0, TAU); g.fill();
    g.translate(x, top + h); g.rotate(tilt); g.scale(1 + squash * 0.5, 1 - squash); g.translate(-x, -(top + h));
    const botW = w * 0.76;
    if (s.handle) {
      g.strokeStyle = s.ink; g.lineWidth = 4;
      g.beginPath(); g.arc(x, top + h * 0.1, w * 0.5, Math.PI * 1.08, Math.PI * 1.92); g.stroke();
    }
    g.fillStyle = s.inside;
    g.beginPath(); g.ellipse(x, top, w / 2, h * 0.12, 0, 0, TAU); g.fill();
    if (inner) inner();
    const bodyPath = () => {
      g.beginPath();
      g.moveTo(x - w / 2, top); g.lineTo(x + w / 2, top);
      g.lineTo(x + botW / 2, top + h - 6);
      g.quadraticCurveTo(x + botW / 2, top + h, x + botW / 2 - 6, top + h);
      g.lineTo(x - botW / 2 + 6, top + h);
      g.quadraticCurveTo(x - botW / 2, top + h, x - botW / 2, top + h - 6);
      g.closePath();
    };
    bodyPath();
    g.fillStyle = s.body; g.fill();
    g.save(); g.clip();
    if (s.stripe) { g.fillStyle = s.stripe; g.fillRect(x - w, top + h * 0.45, w * 2, h * 0.16); }
    if (s.weave) {
      g.strokeStyle = s.weave; g.lineWidth = 2;
      for (let yy = top + h * 0.22; yy < top + h; yy += h * 0.2) { g.beginPath(); g.moveTo(x - w, yy); g.lineTo(x + w, yy); g.stroke(); }
      for (let xx = x - w; xx < x + w; xx += w * 0.14) { g.beginPath(); g.moveTo(xx, top); g.lineTo(xx + w * 0.05, top + h); g.stroke(); }
    }
    g.restore();
    bodyPath();
    g.lineWidth = 3; g.strokeStyle = s.ink; g.lineJoin = 'round'; g.stroke();
    if (s.glow) { g.shadowColor = s.rim; g.shadowBlur = 14; }
    g.beginPath(); g.ellipse(x, top, w / 2, h * 0.12, 0, 0, TAU);
    g.lineWidth = 5; g.strokeStyle = s.rim; g.stroke();
    g.restore();
  }
  function cloud(g, x, y, s, color) {
    g.fillStyle = color;
    g.beginPath();
    g.arc(x, y, 20 * s, 0, TAU); g.arc(x + 24 * s, y - 9 * s, 26 * s, 0, TAU); g.arc(x + 52 * s, y, 18 * s, 0, TAU);
    g.rect(x, y, 52 * s, 18 * s);
    g.fill();
  }
  function tri(g, ax, ay, bx, by, cx, cy) { g.beginPath(); g.moveTo(ax, ay); g.lineTo(bx, by); g.lineTo(cx, cy); g.closePath(); g.fill(); }

  // ---------- Themes ----------
  const stars = Array.from({ length: 70 }, () => ({ x: Math.random(), y: Math.random(), r: rand(0.6, 1.8), p: rand(0, TAU) }));
  const flakes = Array.from({ length: 60 }, () => ({ x: Math.random(), y: Math.random(), r: rand(1, 2.6), v: rand(0.03, 0.08) }));
  const clouds = [{ x: 0.1, y: 0.16, s: 1, v: 9 }, { x: 0.55, y: 0.28, s: 0.7, v: 6 }, { x: 0.85, y: 0.1, s: 0.85, v: 12 }];
  function drawClouds(g, W, H, dt, color) {
    for (const c of clouds) {
      if (!reduceMotion) c.x += (c.v * dt) / Math.max(W, 1);
      if (c.x > 1.15) c.x = -0.2;
      cloud(g, c.x * W, c.y * H * 0.55 + 40, c.s * clamp(W / 700, 0.6, 1.2), color);
    }
  }
  function skyGradient(g, top, bottom, to) {
    const s = g.createLinearGradient(0, 0, 0, to);
    s.addColorStop(0, top); s.addColorStop(1, bottom);
    return s;
  }
  function pine(g, x, base, s, color) {
    g.fillStyle = '#6b4a2f'; g.fillRect(x - 3 * s, base - 8 * s, 6 * s, 10 * s);
    g.fillStyle = color;
    tri(g, x, base - 52 * s, x - 18 * s, base - 22 * s, x + 18 * s, base - 22 * s);
    tri(g, x, base - 38 * s, x - 22 * s, base - 6 * s, x + 22 * s, base - 6 * s);
  }

  const THEMES = {
    park: {
      name: 'Park', accent: '#3a9a4a', bg: '#9fd6f2', ink: '#24341f',
      goodName: 'Tennis ball', badName: 'Rock', specialName: 'Bouncy ball', specialPts: 2, specialType: 'bounce',
      catcher: { body: '#c98f55', rim: '#9a6534', inside: '#5e3d20', weave: '#a8733f', handle: true, ink: '#3b2a18', shadow: 'rgba(30,60,20,0.25)' },
      groundBurst: ['#5aa646', '#79c262'],
      scene(g, W, H, t, dt) {
        const hz = H * 0.62;
        g.fillStyle = skyGradient(g, '#9fd6f2', '#e2f4fc', hz); g.fillRect(0, 0, W, hz + 2);
        circle(g, W * 0.18, H * 0.16, Math.min(W, H) * 0.055, '#ffdf5e');
        drawClouds(g, W, H, dt, '#ffffff');
        g.fillStyle = '#a9d98f';
        g.beginPath(); g.ellipse(W * 0.25, hz + 10, W * 0.4, H * 0.08, 0, 0, TAU); g.ellipse(W * 0.8, hz + 14, W * 0.38, H * 0.07, 0, 0, TAU); g.fill();
        for (const f of [0.12, 0.42, 0.88]) {
          const tx = W * f, ty = hz - H * 0.02, s = clamp(H / 700, 0.6, 1.2);
          g.fillStyle = '#7a5434'; g.fillRect(tx - 4 * s, ty, 8 * s, 30 * s);
          circle(g, tx, ty - 6 * s, 24 * s, '#4f9e4a');
        }
        const gy = hz + H * 0.04, band = Math.max(18, H * 0.04);
        for (let y = gy, i = 0; y < H; y += band, i++) {
          g.fillStyle = i % 2 ? '#6db657' : '#79c262';
          g.fillRect(0, y, W, band + 1);
        }
      },
      good(g, x, y, r, rot) {
        circle(g, x, y, r, '#d4e84a');
        g.save(); g.beginPath(); g.arc(x, y, r, 0, TAU); g.clip();
        g.translate(x, y); g.rotate(rot);
        g.strokeStyle = '#ffffff'; g.lineWidth = Math.max(2, r * 0.13);
        g.beginPath(); g.arc(-r * 1.15, 0, r * 0.78, -Math.PI / 2.4, Math.PI / 2.4); g.stroke();
        g.beginPath(); g.arc(r * 1.15, 0, r * 0.78, Math.PI - Math.PI / 2.4, Math.PI + Math.PI / 2.4); g.stroke();
        g.restore();
        circle(g, x, y, r, null, '#24341f');
      },
      special(g, x, y, r, rot) {
        circle(g, x, y, r, '#e8443a');
        g.save(); g.beginPath(); g.arc(x, y, r, 0, TAU); g.clip();
        g.translate(x, y); g.rotate(rot);
        g.fillStyle = '#ffd54a'; g.fillRect(-r, -r * 0.16, r * 2, r * 0.32);
        g.restore();
        circle(g, x, y, r, null, '#24341f');
        shine(g, x, y, r);
      },
      bad(g, x, y, r, rot, seed) {
        g.save(); g.translate(x, y); g.rotate(rot * 0.3);
        g.beginPath();
        for (let i = 0; i < 8; i++) {
          const a = i * TAU / 8, rr = r * (seed ? seed[i] : 0.9);
          g.lineTo(Math.cos(a) * rr, Math.sin(a) * rr);
        }
        g.closePath(); g.fillStyle = '#8c929b'; g.fill();
        g.lineWidth = Math.max(1.5, r * 0.08); g.strokeStyle = '#3a3f46'; g.lineJoin = 'round'; g.stroke();
        circle(g, -r * 0.25, r * 0.15, r * 0.14, '#737983');
        circle(g, r * 0.3, -r * 0.2, r * 0.1, '#737983');
        g.restore();
      }
    },
    night: {
      name: 'Night', accent: '#6a5cff', bg: '#141c40', ink: '#0b1030',
      goodName: 'Glow ball', badName: 'Spike mine', specialName: 'Shooting star', specialPts: 3, specialType: 'shoot',
      catcher: { body: '#2c3a80', rim: '#3fe0c5', inside: '#0e1533', glow: true, handle: false, ink: '#0b1030', shadow: 'rgba(63,224,197,0.18)' },
      groundBurst: ['#3fe0c5', '#7fd8ff'],
      glowBonus: true,
      scene(g, W, H, t) {
        g.fillStyle = skyGradient(g, '#0d1430', '#2d2f6e', H * 0.75); g.fillRect(0, 0, W, H);
        for (const s of stars) {
          g.globalAlpha = reduceMotion ? 0.8 : 0.45 + 0.55 * (Math.sin(t * 2 + s.p) + 1) / 2;
          circle(g, s.x * W, s.y * H * 0.62, s.r, '#ffffff');
        }
        g.globalAlpha = 1;
        const mr = Math.min(W, H) * 0.06;
        g.save(); g.shadowColor = 'rgba(255,250,210,0.6)'; g.shadowBlur = 30;
        circle(g, W * 0.78, H * 0.18, mr, '#f6f2d4'); g.restore();
        circle(g, W * 0.78 - mr * 0.3, H * 0.18 - mr * 0.2, mr * 0.18, '#e2dcb6');
        circle(g, W * 0.78 + mr * 0.35, H * 0.18 + mr * 0.25, mr * 0.12, '#e2dcb6');
        g.fillStyle = '#1e2758';
        g.beginPath(); g.ellipse(W * 0.2, H * 0.74, W * 0.45, H * 0.1, 0, 0, TAU); g.ellipse(W * 0.85, H * 0.76, W * 0.4, H * 0.09, 0, 0, TAU); g.fill();
        g.fillStyle = '#151c44'; g.fillRect(0, H * 0.76, W, H * 0.24);
      },
      good(g, x, y, r) {
        g.save(); g.shadowColor = '#7fd8ff'; g.shadowBlur = r * 1.1;
        circle(g, x, y, r, '#7fd8ff'); g.restore();
        circle(g, x, y, r * 0.55, '#c9f0ff');
        shine(g, x, y, r);
      },
      special(g, x, y, r, rot) {
        g.save(); g.shadowColor = '#fff3b0'; g.shadowBlur = r * 1.2;
        starPath(g, x, y, r * 1.15, r * 0.5, 5, rot);
        g.fillStyle = '#fff3b0'; g.fill();
        g.restore();
        starPath(g, x, y, r * 1.15, r * 0.5, 5, rot);
        g.lineWidth = Math.max(1.5, r * 0.08); g.strokeStyle = '#c99a2e'; g.lineJoin = 'round'; g.stroke();
      },
      bad(g, x, y, r, rot) { spiky(g, x, y, r, rot, '#ff5b6e', '#ff9aa6', '#7a1626'); }
    },
    snow: {
      name: 'Snow', accent: '#3d7fc4', bg: '#bcd7ec', ink: '#2a3440',
      goodName: 'Snowball', badName: 'Icicle', specialName: 'Snowflake', specialPts: 3, specialType: 'zig',
      catcher: { body: '#d94a4a', rim: '#b33636', inside: '#6e1f1f', stripe: '#ffffff', handle: true, ink: '#2a3440', shadow: 'rgba(60,90,120,0.22)' },
      groundBurst: ['#ffffff', '#dceaf5'],
      unlock: { theme: 'park', score: 100, text: 'Score 100 in Park' },
      scene(g, W, H, t, dt) {
        const hz = H * 0.62;
        g.fillStyle = skyGradient(g, '#a9c9e3', '#eef5fb', hz); g.fillRect(0, 0, W, hz + 2);
        circle(g, W * 0.75, H * 0.15, Math.min(W, H) * 0.05, '#fff7d6');
        for (const [fx, fh, fw] of [[0.2, 0.24, 0.32], [0.55, 0.3, 0.38], [0.9, 0.22, 0.3]]) {
          const px = fx * W, py = hz - fh * H, bw = fw * W;
          g.fillStyle = '#c3d5e4'; tri(g, px, py, px - bw / 2, hz + 2, px + bw / 2, hz + 2);
          g.fillStyle = '#ffffff'; tri(g, px, py, px - bw * 0.13, py + fh * H * 0.27, px + bw * 0.13, py + fh * H * 0.27);
        }
        g.fillStyle = '#f4f8fc'; g.fillRect(0, hz, W, H - hz);
        g.fillStyle = '#ffffff';
        g.beginPath(); g.ellipse(W * 0.3, hz + 6, W * 0.38, H * 0.05, 0, 0, TAU); g.ellipse(W * 0.82, hz + 10, W * 0.3, H * 0.045, 0, 0, TAU); g.fill();
        const ps = clamp(H / 700, 0.6, 1.2);
        for (const f of [0.08, 0.16, 0.66, 0.93]) pine(g, W * f, hz + 10, ps, '#3f6b5a');
        g.fillStyle = 'rgba(255,255,255,0.9)';
        for (const f of flakes) {
          if (!reduceMotion) { f.y += f.v * dt; f.x += Math.sin(t + f.r * 3) * 0.004 * dt; }
          if (f.y > 1) { f.y = -0.02; f.x = Math.random(); }
          g.beginPath(); g.arc(f.x * W, f.y * H, f.r, 0, TAU); g.fill();
        }
      },
      good(g, x, y, r) {
        circle(g, x, y, r, '#ffffff');
        g.save(); g.beginPath(); g.arc(x, y, r, 0, TAU); g.clip();
        circle(g, x + r * 0.35, y + r * 0.4, r * 0.85, '#e1ecf5');
        g.restore();
        circle(g, x, y, r, null, '#6f8fa8');
        circle(g, x - r * 0.3, y - r * 0.2, r * 0.08, '#c9dbe8');
        circle(g, x + r * 0.2, y - r * 0.4, r * 0.06, '#c9dbe8');
      },
      special(g, x, y, r, rot) {
        circle(g, x, y, r, '#7ec8f0', '#2f6f99');
        g.save(); g.translate(x, y); g.rotate(rot * 0.4);
        g.strokeStyle = '#ffffff'; g.lineWidth = Math.max(1.5, r * 0.1); g.lineCap = 'round';
        for (let i = 0; i < 6; i++) {
          g.rotate(Math.PI / 3);
          g.beginPath(); g.moveTo(0, 0); g.lineTo(0, -r * 0.7);
          g.moveTo(0, -r * 0.42); g.lineTo(-r * 0.16, -r * 0.58);
          g.moveTo(0, -r * 0.42); g.lineTo(r * 0.16, -r * 0.58);
          g.stroke();
        }
        g.restore();
      },
      bad(g, x, y, r) {
        g.beginPath();
        g.moveTo(x - r * 0.6, y - r * 1.1); g.lineTo(x + r * 0.6, y - r * 1.1); g.lineTo(x, y + r * 1.25); g.closePath();
        g.fillStyle = '#a8d8f0'; g.fill();
        g.lineWidth = Math.max(1.5, r * 0.08); g.strokeStyle = '#3f7896'; g.lineJoin = 'round'; g.stroke();
        g.beginPath(); g.moveTo(x - r * 0.25, y - r * 0.95); g.lineTo(x - r * 0.05, y + r * 0.5);
        g.strokeStyle = 'rgba(255,255,255,0.8)'; g.stroke();
      }
    }
  };

  // ---------- Saved choices ----------
  const bests = {};
  for (const k of Object.keys(THEMES)) bests[k] = parseInt(store(bestKey(k)), 10) || 0;
  function isLocked(k) {
    const u = THEMES[k].unlock;
    return !!u && bests[u.theme] < u.score;
  }
  let themeKey = store(THEME_KEY);
  if (!THEMES[themeKey] || isLocked(themeKey)) themeKey = 'park';
  let theme = THEMES[themeKey];
  let diffKey = DIFFS[store(DIFF_KEY)] ? store(DIFF_KEY) : 'normal';
  let D = DIFFS[diffKey];

  function drawItem(g, b, T) {
    const th = T || theme;
    if (b.kind === 'bonus') goldBall(g, b.x, b.y, b.r, b.rot, th.ink, th.glowBonus);
    else if (b.kind === 'bad') th.bad(g, b.x, b.y, b.r, b.rot, b.seed);
    else if (b.kind === 'special') th.special(g, b.x, b.y, b.r, b.rot);
    else if (b.kind === 'power') drawPower(g, b.x, b.y, b.r, b.power, time);
    else th.good(g, b.x, b.y, b.r, b.rot);
  }

  // ---------- State ----------
  let W = 0, H = 0;
  let state = 'ready'; // ready | playing | paused | over
  const player = { x: 0, targetX: 0, baseW: 110, w: 110, h: 76, vx: 0, wMul: 1, squash: 0 };
  let balls = [], sinking = [], particles = [], texts = [];
  let maxLives = D.lives;
  let score = 0, lives = maxLives, combo = 0, topCombo = 0, caught = 0, level = 1;
  const powers = { wide: 0, slow: 0, magnet: 0, shield: 0 };
  let spawnTimer = 0.4, shake = 0, time = 0;
  let pointerDown = false;
  const keys = { left: false, right: false };

  const bucketTop = () => H - player.h - Math.max(18, H * 0.035);
  const ballRadius = () => clamp(Math.min(W, H) * 0.038, 15, 26);

  function resize() {
    const dpr = Math.min(window.devicePixelRatio || 1, 2);
    const r = cvs.getBoundingClientRect();
    W = r.width; H = r.height;
    cvs.width = Math.round(W * dpr); cvs.height = Math.round(H * dpr);
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    player.baseW = clamp(W * 0.2, 84, 132);
    player.w = player.baseW * player.wMul;
    player.h = player.baseW * 0.7;
    if (!player.x) player.x = player.targetX = W / 2;
    player.x = clamp(player.x, player.w / 2, W - player.w / 2);
    player.targetX = clamp(player.targetX, player.w / 2, W - player.w / 2);
  }

  // ---------- Sound ----------
  let actx = null;
  let muted = store(MUTE_KEY) === '1';
  function ensureAudio() {
    if (!actx) { try { actx = new (window.AudioContext || window.webkitAudioContext)(); } catch (e) { actx = null; } }
    if (actx && actx.state === 'suspended') actx.resume();
  }
  function tone(freq, dur, type, vol, when, slideTo) {
    if (!actx || muted) return;
    const t = actx.currentTime + (when || 0);
    const o = actx.createOscillator(), g = actx.createGain();
    o.type = type || 'sine';
    o.frequency.setValueAtTime(freq, t);
    if (slideTo) o.frequency.exponentialRampToValueAtTime(slideTo, t + dur);
    g.gain.setValueAtTime(0.0001, t);
    g.gain.exponentialRampToValueAtTime(vol || 0.12, t + 0.012);
    g.gain.exponentialRampToValueAtTime(0.0001, t + dur);
    o.connect(g).connect(actx.destination);
    o.start(t); o.stop(t + dur + 0.03);
  }
  const sfx = {
    catch: m => tone(500 + m * 70, 0.12, 'triangle', 0.14, 0, 900 + m * 70),
    bonus: () => [660, 880, 1100, 1320].forEach((f, i) => tone(f, 0.13, 'triangle', 0.12, i * 0.06)),
    special: () => [740, 990, 1240].forEach((f, i) => tone(f, 0.12, 'sine', 0.13, i * 0.05)),
    power: () => [392, 523, 659, 784, 1046].forEach((f, i) => tone(f, 0.12, 'square', 0.05, i * 0.05)),
    block: () => tone(900, 0.18, 'triangle', 0.12, 0, 1400),
    bounce: () => tone(260, 0.12, 'sine', 0.12, 0, 520),
    hurt: () => tone(190, 0.32, 'sawtooth', 0.1, 0, 70),
    miss: () => tone(420, 0.28, 'sine', 0.13, 0, 150),
    level: () => [523, 659, 784, 1046].forEach((f, i) => tone(f, 0.16, 'square', 0.05, i * 0.08)),
    over: () => [392, 330, 262, 196].forEach((f, i) => tone(f, 0.28, 'triangle', 0.12, i * 0.15))
  };

  // ---------- Gameplay ----------
  function spawnBall(demo) {
    const r = ballRadius();
    const badChance = demo ? 0.15 : Math.min((0.12 + level * 0.016) * D.bad, 0.4);
    const powerChance = demo ? 0 : 0.035;
    const roll = Math.random();
    let kind = 'good';
    if (roll < badChance) kind = 'bad';
    else if (roll < badChance + powerChance) kind = 'power';
    else if (roll < badChance + powerChance + 0.05) kind = 'bonus';
    else if (roll < badChance + powerChance + 0.12) kind = 'special';
    const base = H * (demo ? 0.28 : (0.3 + level * 0.045) * D.speed);
    let vy = Math.min(base * rand(0.85, 1.15), H * 1.1);
    if (kind === 'bonus') vy *= 1.15;
    if (kind === 'power') vy *= 0.9;
    const sway = demo ? 0 : Math.max(0, level - 2) * 7;
    const b = {
      kind, r, x: rand(r + 6, W - r - 6), y: -r - 4, prevY: -r - 4,
      vy, vx: rand(-sway, sway), rot: rand(0, TAU), vr: rand(-3, 3),
      seed: Array.from({ length: 8 }, () => rand(0.78, 1.05))
    };
    if (kind === 'power') { const keysP = Object.keys(POWERS); b.power = keysP[Math.floor(Math.random() * keysP.length)]; b.vr = 0; b.rot = 0; }
    if (kind === 'special') {
      const type = theme.specialType;
      if (type === 'shoot') { b.vy *= 1.45; b.vx = (b.x < W / 2 ? 1 : -1) * H * rand(0.25, 0.4); b.trail = []; }
      if (type === 'zig') { b.phase = 0; b.amp = rand(40, 70); }
    }
    balls.push(b);
  }
  function burst(x, y, colors, n, power) {
    const count = reduceMotion ? Math.ceil(n / 3) : n;
    for (let i = 0; i < count; i++) {
      const a = rand(Math.PI * 1.05, Math.PI * 1.95), s = rand(0.4, 1) * power;
      particles.push({ x, y, vx: Math.cos(a) * s, vy: Math.sin(a) * s, life: rand(0.5, 0.9), max: 0.9, r: rand(2, 4), c: colors[i % colors.length] });
    }
  }
  function floatText(x, y, txt, color, size) { texts.push({ x, y, txt, color, size: size || 22, life: 1 }); }
  const multiplier = () => Math.min(5, 1 + Math.floor(combo / 5));

  function dropIn(b) {
    sinking.push({ b, t: 0, y0: b.y, r0: b.r, dx: b.x - player.x });
    player.squash = reduceMotion ? 0 : 0.14;
  }

  function onCatch(b, demo) {
    const top = bucketTop();
    if (b.kind === 'bad') {
      if (demo) return;
      if (powers.shield > 0) {
        powers.shield = 0;
        burst(b.x, top, [POWERS.shield.color, '#ffffff'], 14, 240);
        floatText(b.x, top - 20, 'Blocked!', '#ffffff', 26);
        sfx.block();
        return;
      }
      lives--; combo = 0;
      shake = reduceMotion ? 0 : 10;
      burst(b.x, top, ['#888888', '#555555'], 12, 240);
      floatText(b.x, top - 20, 'Ouch!', '#ff5b5b', 26);
      sfx.hurt();
      if (lives <= 0) endGame();
      return;
    }
    dropIn(b);
    if (b.kind === 'power') {
      const p = POWERS[b.power];
      powers[b.power] = p.dur;
      burst(b.x, top, [p.color, '#ffffff'], 16, 250);
      floatText(W / 2, H * 0.4, p.name + '!', p.color, 30);
      sfx.power();
      return;
    }
    burst(b.x, top, b.kind === 'bonus' ? ['#ffc21a', '#ffffff'] : [theme.accent, '#ffffff', '#ffd54a'], 14, 230);
    if (demo) return;
    const prevMult = multiplier();
    combo++; caught++;
    topCombo = Math.max(topCombo, combo);
    const m = multiplier();
    const base = b.kind === 'bonus' ? 5 : b.kind === 'special' ? theme.specialPts : 1;
    const pts = base * m;
    score += pts;
    floatText(b.x, top - 18, '+' + pts, b.kind === 'bonus' ? '#ffc21a' : '#ffffff', base > 1 ? 28 : 22);
    if (b.kind === 'bonus') sfx.bonus(); else if (b.kind === 'special') sfx.special(); else sfx.catch(m);
    if (m > prevMult) floatText(W / 2, H * 0.34, 'Combo ×' + m, '#ffd54a', 30);
    const newLevel = 1 + Math.floor(caught / 10);
    if (newLevel > level) {
      level = newLevel;
      floatText(W / 2, H * 0.27, 'Level ' + level, '#ffffff', 38);
      sfx.level();
    }
  }
  function onMiss(b, demo) {
    if (demo || b.kind === 'bad' || b.kind === 'power') return;
    lives--; combo = 0;
    shake = reduceMotion ? 0 : 6;
    floatText(clamp(b.x, 40, W - 40), H - 50, 'Missed', '#ff5b5b', 20);
    sfx.miss();
    if (lives <= 0) endGame();
  }

  function update(dt) {
    const demo = state !== 'playing';
    time += dt;

    for (const k in powers) if (powers[k] > 0) powers[k] = Math.max(0, powers[k] - dt);
    player.wMul += ((powers.wide > 0 ? 1.6 : 1) - player.wMul) * Math.min(1, dt * 8);
    player.w = player.baseW * player.wMul;
    player.squash = Math.max(0, player.squash - dt * 0.9);
    const bdt = dt * (powers.slow > 0 ? 0.5 : 1);

    spawnTimer -= bdt;
    if (spawnTimer <= 0) {
      spawnBall(demo);
      spawnTimer = (demo ? 1.1 : Math.max(0.36, 1.05 - (level - 1) * 0.07) * D.spawn) * rand(0.75, 1.25);
    }

    const half = player.w / 2;
    if (demo) {
      let target = null;
      for (const b of balls) if (b.kind !== 'bad' && b.y < bucketTop() && (!target || b.y > target.y)) target = b;
      player.targetX = target ? target.x : W / 2;
    } else if (keys.left || keys.right) {
      const speed = Math.max(W * 0.95, 520);
      player.targetX = player.x + ((keys.right ? 1 : 0) - (keys.left ? 1 : 0)) * speed * dt * 1.2;
    }
    player.targetX = clamp(player.targetX, half, W - half);
    const prevX = player.x;
    player.x += (player.targetX - player.x) * Math.min(1, dt * (demo ? 5 : 20));
    player.x = clamp(player.x, half, W - half);
    player.vx = (player.x - prevX) / Math.max(dt, 0.001);

    const top = bucketTop();
    const opening = player.w * 0.5;
    for (let i = balls.length - 1; i >= 0; i--) {
      const b = balls[i];
      b.prevY = b.y;
      b.y += b.vy * bdt; b.x += b.vx * bdt; b.rot += b.vr * bdt;
      if (b.kind === 'special') {
        if (theme.specialType === 'zig') { b.phase += bdt; b.x += Math.cos(b.phase * 4) * b.amp * 4 * bdt; }
        if (theme.specialType === 'shoot' && b.trail) { b.trail.push({ x: b.x, y: b.y }); if (b.trail.length > 10) b.trail.shift(); }
        if (theme.specialType === 'bounce' && b.bounced) b.vy += H * 1.2 * bdt;
      }
      if (powers.magnet > 0 && !demo && b.kind !== 'bad' && b.y > H * 0.15 && b.y < top) {
        const dx = player.x - b.x;
        b.x += Math.sign(dx) * Math.min(Math.abs(dx), Math.max(260, W * 0.4) * dt);
      }
      if (b.x < b.r || b.x > W - b.r) { b.vx *= -1; b.x = clamp(b.x, b.r, W - b.r); }
      const crossed = b.prevY + b.r * 0.4 < top && b.y + b.r * 0.4 >= top;
      if (crossed && Math.abs(b.x - player.x) <= opening + b.r * 0.35) {
        balls.splice(i, 1);
        onCatch(b, demo);
        if (state === 'over' && !demo) break;
        continue;
      }
      if (b.kind === 'special' && theme.specialType === 'bounce' && !b.bounced && b.y + b.r >= H - 4 && b.vy > 0) {
        b.bounced = true;
        b.y = H - 4 - b.r;
        b.vy = -Math.sqrt(2 * H * 1.2 * H * 0.35); // rises about a third of the screen, then falls again
        b.vx += rand(-60, 60);
        burst(b.x, H - 4, theme.groundBurst, 8, 160);
        if (!demo) { floatText(b.x, H - 60, 'Boing!', '#ffffff', 20); sfx.bounce(); }
        continue;
      }
      if (b.y - b.r > H) { balls.splice(i, 1); onMiss(b, demo); }
    }

    for (let i = sinking.length - 1; i >= 0; i--) {
      const s = sinking[i];
      s.t += dt;
      const p = Math.min(1, s.t / 0.22);
      s.b.x = player.x + s.dx * (1 - p);
      s.b.y = s.y0 + (top + player.h * 0.25 - s.y0) * p;
      s.b.r = s.r0 * (1 - 0.35 * p);
      if (p >= 1) sinking.splice(i, 1);
    }
    for (let i = particles.length - 1; i >= 0; i--) {
      const p = particles[i];
      p.life -= dt;
      if (p.life <= 0) { particles.splice(i, 1); continue; }
      p.vy += 700 * dt; p.x += p.vx * dt; p.y += p.vy * dt;
    }
    for (let i = texts.length - 1; i >= 0; i--) {
      const t = texts[i];
      t.life -= dt * 0.9; t.y -= 46 * dt;
      if (t.life <= 0) texts.splice(i, 1);
    }
    shake = Math.max(0, shake - dt * 40);
  }

  function render(dt) {
    ctx.save();
    if (shake > 0) ctx.translate(rand(-shake, shake), rand(-shake, shake) * 0.6);
    theme.scene(ctx, W, H, time, state === 'paused' ? 0 : dt * (powers.slow > 0 ? 0.5 : 1));

    for (const b of balls) {
      if (b.trail) {
        b.trail.forEach((p, i) => {
          ctx.globalAlpha = (i / b.trail.length) * 0.5;
          circle(ctx, p.x, p.y, b.r * 0.6 * (i / b.trail.length), '#fff3b0');
        });
        ctx.globalAlpha = 1;
      }
      drawItem(ctx, b);
    }

    const top = bucketTop();
    const squash = Math.sin((player.squash / 0.14) * Math.PI) * player.squash;
    bucket(ctx, player.x, top, player.w, player.h, reduceMotion ? 0 : clamp(player.vx / 4000, -0.12, 0.12), squash, theme.catcher,
      () => { for (const s of sinking) drawItem(ctx, s.b); });

    if (powers.shield > 0) {
      ctx.save();
      const fade = powers.shield < 2 ? (Math.sin(time * 20) + 1) / 2 : 1;
      ctx.globalAlpha = 0.35 * fade + 0.1;
      ctx.strokeStyle = POWERS.shield.color; ctx.lineWidth = 4;
      ctx.beginPath(); ctx.ellipse(player.x, top + player.h * 0.45, player.w * 0.7, player.h * 0.95, 0, Math.PI, TAU); ctx.stroke();
      ctx.restore();
    }

    for (const p of particles) {
      ctx.globalAlpha = clamp(p.life / p.max, 0, 1);
      circle(ctx, p.x, p.y, p.r, p.c);
    }
    ctx.globalAlpha = 1;
    ctx.textAlign = 'center'; ctx.textBaseline = 'middle';
    for (const t of texts) {
      ctx.globalAlpha = clamp(t.life * 1.4, 0, 1);
      ctx.font = `800 ${t.size}px system-ui, -apple-system, "Segoe UI", sans-serif`;
      ctx.lineJoin = 'round'; ctx.lineWidth = 4; ctx.strokeStyle = 'rgba(0,0,0,0.55)';
      ctx.strokeText(t.txt, t.x, t.y);
      ctx.fillStyle = t.color; ctx.fillText(t.txt, t.x, t.y);
    }
    ctx.globalAlpha = 1;
    ctx.restore();
  }

  // ---------- HUD ----------
  const heartSvg = '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M12 21s-7.5-4.6-9.6-9.3C.9 8.3 3 4.5 6.7 4.5c2.2 0 3.6 1.3 5.3 3.2 1.7-1.9 3.1-3.2 5.3-3.2 3.7 0 5.8 3.8 4.3 7.2C19.5 16.4 12 21 12 21z" fill="#ff4d5e" stroke="#ffffff" stroke-width="2"/></svg>';
  const lockSvg = '<svg viewBox="0 0 24 24" aria-hidden="true"><rect x="5" y="10" width="14" height="10" rx="2" fill="currentColor"/><path d="M8 10V7a4 4 0 0 1 8 0v3" fill="none" stroke="currentColor" stroke-width="2.2"/></svg>';
  const icons = {
    pause: '<svg viewBox="0 0 24 24" aria-hidden="true"><rect x="6" y="5" width="4" height="14" rx="1" fill="currentColor"/><rect x="14" y="5" width="4" height="14" rx="1" fill="currentColor"/></svg>',
    play: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M8 5v14l11-7z" fill="currentColor"/></svg>',
    sound: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M4 9h4l5-4v14l-5-4H4z" fill="currentColor"/><path d="M16 8.5a5 5 0 0 1 0 7" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"/></svg>',
    mute: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M4 9h4l5-4v14l-5-4H4z" fill="currentColor"/><path d="M16.5 9.5l5 5M21.5 9.5l-5 5" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"/></svg>'
  };
  const livesEl = $('lives');
  const hud = { score: -1, best: -1, lives: -1, max: -1, mult: -1, powers: '' };
  function updateHud() {
    if (hud.score !== score) { $('score').textContent = score; hud.score = score; }
    const shownBest = Math.max(bests[themeKey], state === 'ready' ? 0 : score);
    if (hud.best !== shownBest) { $('best').textContent = shownBest; hud.best = shownBest; }
    if (hud.max !== maxLives) { livesEl.innerHTML = heartSvg.repeat(maxLives); hud.max = maxLives; hud.lives = -1; }
    if (hud.lives !== lives) {
      [...livesEl.children].forEach((h, i) => h.classList.toggle('lost', i >= lives));
      livesEl.setAttribute('aria-label', lives + (lives === 1 ? ' life left' : ' lives left'));
      hud.lives = lives;
    }
    const m = state === 'playing' ? multiplier() : 1;
    if (hud.mult !== m) {
      $('combo').hidden = m < 2;
      $('combo').textContent = 'Combo ×' + m;
      hud.mult = m;
    }
    const active = state === 'ready' ? [] : Object.keys(powers).filter(k => powers[k] > 0);
    const sig = active.map(k => k + Math.ceil(powers[k])).join('|');
    if (hud.powers !== sig) {
      $('powers').innerHTML = active.map(k =>
        `<div class="power-chip"><i style="background:${POWERS[k].color}"></i>${POWERS[k].short} ${Math.ceil(powers[k])}s</div>`).join('');
      hud.powers = sig;
    }
  }
  function updateButtons() {
    const pb = $('pauseBtn');
    const paused = state === 'paused';
    pb.innerHTML = paused ? icons.play : icons.pause;
    pb.setAttribute('aria-label', paused ? 'Resume game' : 'Pause game');
    pb.hidden = !(state === 'playing' || state === 'paused');
    const mb = $('muteBtn');
    mb.innerHTML = muted ? icons.mute : icons.sound;
    mb.setAttribute('aria-label', muted ? 'Turn sound on' : 'Mute sound');
  }

  // ---------- Menu: themes & difficulty ----------
  function drawIcons() {
    document.querySelectorAll('.theme-btn canvas').forEach(c => {
      const g = c.getContext('2d');
      g.clearRect(0, 0, c.width, c.height);
      const T = THEMES[c.dataset.theme];
      circle(g, c.width / 2, c.height / 2, c.width / 2, T.bg);
      drawItem(g, { kind: 'good', x: c.width / 2, y: c.height / 2, r: c.width * 0.26, rot: 0.4 }, T);
    });
  }
  function renderThemes() {
    const el = $('themes');
    el.innerHTML = '';
    for (const key of Object.keys(THEMES)) {
      const T = THEMES[key], locked = isLocked(key);
      const btn = document.createElement('button');
      btn.type = 'button';
      btn.className = 'theme-btn' + (locked ? ' locked' : '');
      btn.setAttribute('role', 'radio');
      btn.setAttribute('aria-checked', String(key === themeKey));
      if (locked) btn.setAttribute('aria-disabled', 'true');
      btn.innerHTML = locked
        ? `<span class="lock">${lockSvg}</span>${T.name}<small>${T.unlock.text}</small>`
        : `<canvas data-theme="${key}" width="72" height="72"></canvas>${T.name}<small>Best ${bests[key]}</small>`;
      btn.addEventListener('click', () => {
        const note = $('themeNote');
        if (locked) {
          note.textContent = `${T.name} is locked. ${T.unlock.text} to unlock it.`;
          note.hidden = false;
          return;
        }
        note.hidden = true;
        applyTheme(key);
      });
      el.appendChild(btn);
    }
    drawIcons();
  }
  function applyTheme(key) {
    themeKey = key; theme = THEMES[key];
    store(THEME_KEY, key);
    const root = document.documentElement.style;
    root.setProperty('--accent', theme.accent);
    root.setProperty('--bg', theme.bg);
    balls = balls.filter(b => b.kind !== 'special');
    renderThemes();
  }
  function renderDiffs() {
    const el = $('diffs');
    el.innerHTML = '';
    for (const key of Object.keys(DIFFS)) {
      const btn = document.createElement('button');
      btn.type = 'button';
      btn.setAttribute('role', 'radio');
      btn.setAttribute('aria-checked', String(key === diffKey));
      btn.textContent = DIFFS[key].label;
      btn.addEventListener('click', () => {
        diffKey = key; D = DIFFS[key];
        store(DIFF_KEY, key);
        maxLives = lives = D.lives;
        renderDiffs();
      });
      el.appendChild(btn);
    }
  }

  // ---------- Flow ----------
  function showOnly(id) {
    for (const c of ['startCard', 'pauseCard', 'overCard']) $(c).hidden = c !== id;
    const btn = id && $(id).querySelector('.btn');
    if (btn) btn.focus({ preventScroll: true });
    cvs.classList.toggle('playing', state === 'playing');
    updateButtons();
  }
  function resetRound() {
    balls = []; sinking = []; particles = []; texts = [];
    maxLives = D.lives;
    score = 0; lives = maxLives; combo = 0; topCombo = 0; caught = 0; level = 1;
    for (const k in powers) powers[k] = 0;
    spawnTimer = 0.6; shake = 0;
  }
  function startGame() {
    ensureAudio();
    resetRound();
    state = 'playing';
    showOnly(null);
  }
  function pauseGame() {
    if (state !== 'playing') return;
    state = 'paused';
    keys.left = keys.right = false;
    showOnly('pauseCard');
  }
  function resumeGame() {
    if (state !== 'paused') return;
    ensureAudio();
    state = 'playing';
    showOnly(null);
  }
  function endGame() {
    state = 'over';
    const lockedBefore = Object.keys(THEMES).filter(isLocked);
    const isBest = score > bests[themeKey];
    if (isBest) { bests[themeKey] = score; store(bestKey(themeKey), score); }
    const unlockedNow = lockedBefore.filter(k => !isLocked(k));
    $('unlockMsg').hidden = unlockedNow.length === 0;
    $('unlockMsg').textContent = unlockedNow.map(k => `New theme unlocked: ${THEMES[k].name}!`).join(' ');
    $('overMeta').textContent = `${theme.name} · ${D.label}`;
    $('finalScore').textContent = score;
    $('newBest').hidden = !isBest || score === 0;
    $('sumCaught').textContent = caught;
    $('sumCombo').textContent = topCombo;
    $('sumLevel').textContent = level;
    for (const k in powers) powers[k] = 0;
    sfx.over();
    cvs.classList.remove('playing');
    updateButtons();
    setTimeout(() => { if (state === 'over') showOnly('overCard'); }, 700);
  }
  function toMenu() {
    state = 'ready';
    resetRound();
    renderThemes();
    showOnly('startCard');
  }

  $('startBtn').addEventListener('click', startGame);
  $('againBtn').addEventListener('click', startGame);
  $('menuBtn').addEventListener('click', toMenu);
  $('resumeBtn').addEventListener('click', resumeGame);
  $('quitBtn').addEventListener('click', () => { if (state === 'paused') endGame(); });
  $('pauseBtn').addEventListener('click', () => (state === 'paused' ? resumeGame() : pauseGame()));
  $('muteBtn').addEventListener('click', () => {
    muted = !muted;
    store(MUTE_KEY, muted ? '1' : '0');
    if (!muted) ensureAudio();
    updateButtons();
  });

  // ---------- Input ----------
  function pointerTo(e) { player.targetX = e.clientX - cvs.getBoundingClientRect().left; }
  cvs.addEventListener('pointerdown', e => { pointerDown = true; if (state === 'playing') pointerTo(e); });
  window.addEventListener('pointermove', e => {
    if (state === 'playing' && (e.pointerType === 'mouse' || pointerDown)) pointerTo(e);
  });
  window.addEventListener('pointerup', () => { pointerDown = false; });
  window.addEventListener('pointercancel', () => { pointerDown = false; });
  window.addEventListener('keydown', e => {
    const k = e.key;
    if (k === 'ArrowLeft' || k === 'a' || k === 'A') { keys.left = true; e.preventDefault(); }
    if (k === 'ArrowRight' || k === 'd' || k === 'D') { keys.right = true; e.preventDefault(); }
    if (e.target && e.target.tagName === 'BUTTON' && (k === ' ' || k === 'Enter')) return;
    if (k === 'p' || k === 'P' || k === 'Escape' || k === ' ') {
      e.preventDefault();
      if (state === 'playing') pauseGame();
      else if (state === 'paused') resumeGame();
      else if (k === ' ' && state === 'ready') startGame();
    }
    if (k === 'Enter' && (state === 'ready' || (state === 'over' && !$('overCard').hidden))) startGame();
  });
  window.addEventListener('keyup', e => {
    const k = e.key;
    if (k === 'ArrowLeft' || k === 'a' || k === 'A') keys.left = false;
    if (k === 'ArrowRight' || k === 'd' || k === 'D') keys.right = false;
  });
  document.addEventListener('visibilitychange', () => { if (document.hidden) pauseGame(); });
  window.addEventListener('blur', pauseGame);
  window.addEventListener('resize', resize);

  // ---------- Loop ----------
  let last = performance.now();
  function frame(now) {
    const dt = Math.min(0.05, (now - last) / 1000);
    last = now;
    if (state !== 'paused') update(dt);
    render(dt);
    updateHud();
    requestAnimationFrame(frame);
  }

  resize();
  applyTheme(themeKey);
  renderDiffs();
  updateButtons();
  updateHud();
  for (let i = 0; i < 4; i++) {
    spawnBall(true);
    balls[balls.length - 1].y = rand(H * 0.1, H * 0.55);
  }
  $('startBtn').focus({ preventScroll: true });
  requestAnimationFrame(frame);
})();
