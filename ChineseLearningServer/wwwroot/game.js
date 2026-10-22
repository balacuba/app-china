// "Mèo bắn bong bóng" (NinjaGame.cs) on a canvas: words float down through a
// night sky inside soap bubbles. Typing a word's characters, or its pinyin
// without tones, makes the mochi cat on the roof throw a ball of yarn that pops
// it (+10 × combo). A word that lands on the roof costs one of three lives.
// Every 8 pops is a new level: faster and more words.
import { mochiCat } from './cats.js';

const MAX_LIVES = 3;
const HITS_PER_LEVEL = 8;
const ROOF_HEIGHT = 26;
const CAT_SIZE = 84;
const YARN_RADIUS = 9;
export const DEFAULT_FALL_SECONDS = 12;

// Pause after typing before it is checked. When the text could still grow into
// another falling word (你 → 你好, ni → nihao), the shorter word is only shot
// once nothing has been typed for 0.2 seconds in all.
const AUTO_HIT_DELAY = 150;
const PREFIX_HIT_DELAY = 200 - AUTO_HIT_DELAY;

const TEXT_COLORS = ['#67e8f9', '#f472b6', '#fde047', '#86efac', '#c4b5fd', '#fdba74'];
const TYPED_COLOR = '#f83838';
const FOAM_COLORS = ['#c8eeff', '#ffbade', '#fff0a5', '#d6c4ff', '#ffffff'];
const HANZI_FONT = '"Microsoft YaHei UI", "PingFang SC", "Noto Sans SC", "Hiragino Sans GB", sans-serif';
const UI_FONT = '"Segoe UI", system-ui, -apple-system, Roboto, sans-serif';
const WORD_PAD = 4;

const MODES = [
  { id: 'hanzi', name: '汉字' },
  { id: 'both', name: '汉字 + Pinyin' },
  { id: 'pinyin', name: 'Pinyin' }
];

// Kept between visits to the page, like the desktop game's mode.
let mode = 'hanzi';

// Pinyin reduced to its bare letters: no tones (marks or numbers), spaces or
// apostrophes, lower case, ü as u.
function pinyinKey(text) {
  return String(text ?? '').normalize('NFD').toLowerCase().replace(/v/g, 'u').replace(/[^a-z]/g, '');
}

function isHanzi(character) {
  return (character >= '㐀' && character <= '鿿') || (character >= '豈' && character <= '﫿');
}

// A pinyin letter without its tone mark, lower case; v stands for ü.
function baseLetter(letter) {
  const plain = letter.normalize('NFD')[0].toLowerCase();
  return plain === 'v' ? 'u' : plain;
}

const isLetter = character => /\p{L}/u.test(character);

// How many leading characters of the word the typed text matches.
function typedHanziLength(chinese, typed) {
  return typed.length > 0 && chinese.startsWith(typed) ? typed.length : 0;
}

// How much of the pinyin to colour: the syllables of the characters typed so far,
// or, when pinyin is typed, the letters it matches (tones and spaces ignored).
function typedPinyinLength(word, typed, hanziTyped) {
  if (hanziTyped > 0) {
    const syllables = word.Pinyin.split(' ').filter(Boolean);
    if (syllables.length !== [...word.Chinese].length) {
      return hanziTyped === word.Chinese.length ? word.Pinyin.length : 0;
    }
    let end = 0;
    for (let index = 0; index < hanziTyped; index++) {
      end = word.Pinyin.indexOf(syllables[index], end) + syllables[index].length;
    }
    return end;
  }
  const letters = [...typed].filter(isLetter).map(baseLetter);
  if (letters.length === 0) return 0;
  let matched = 0;
  for (let position = 0; position < word.Pinyin.length; position++) {
    const letter = word.Pinyin[position];
    if (!isLetter(letter)) continue;
    if (baseLetter(letter) !== letters[matched]) return 0;
    if (++matched === letters.length) return position + 1;
  }
  return 0;
}

// A word is answered by its characters, or in any mode by its pinyin typed
// without tones.
function isAnswer(falling, typed, typedKey) {
  return falling.word.Chinese === typed || (typedKey.length > 0 && typedKey === falling.key);
}

// Could what has been typed still grow into this (longer) word: 你 → 你好, ni → nihao.
function couldBecome(falling, typed, typedKey) {
  const chinese = falling.word.Chinese;
  if (chinese.length > typed.length && chinese.startsWith(typed)) return true;
  return typedKey.length > 0 && falling.key.length > typedKey.length && falling.key.startsWith(typedKey);
}

// Does the typing still lead to this word? Its characters must start the word,
// and any pinyin letters after them must start the pinyin of the syllables
// still to come, so 你, 你h, nih and nihao all lead to 你好.
function fits(word, characters, letters) {
  if (!word.Chinese.startsWith(characters)) return false;
  if (letters.length === 0) return true;
  const syllables = word.Pinyin.split(' ').filter(Boolean);
  const rest = syllables.length === word.Chinese.length
    ? syllables.slice(characters.length).join('')
    : characters.length === 0 ? word.Pinyin : '';
  return pinyinKey(rest).startsWith(letters);
}

// A soap bubble round the word: slightly oval for long words.
function bubbleRadii(size) {
  const across = size.width / 2 * 1.18 + 14;
  const down = Math.max(size.height / 2 * 1.18 + 14, across * 0.72);
  return { across: Math.max(across, down * 0.92), down };
}

function hitRadius(falling) {
  const radii = bubbleRadii(falling.size);
  return Math.min(radii.across, radii.down) * 1.05;
}

function mix(from, to, amount) {
  return from.map((value, index) => Math.round(value + (to[index] - value) * amount));
}

const rgb = (color, alpha = 1) => `rgba(${color[0]},${color[1]},${color[2]},${alpha})`;

function offscreen(width, height, scale) {
  const canvas = document.createElement('canvas');
  canvas.width = Math.max(1, Math.ceil(width * scale));
  canvas.height = Math.max(1, Math.ceil(height * scale));
  const context = canvas.getContext('2d');
  context.scale(scale, scale);
  return { canvas, context };
}

// The bubble itself never changes, so each size is drawn once: a clear film
// tinted blue towards its edge, a faint pink sheen, a rainbow rim and white
// window reflections.
const bubbleCache = new Map();

function bubbleSprite(across, down, scale) {
  const id = `${across}x${down}@${scale}`;
  let sprite = bubbleCache.get(id);
  if (sprite) return sprite;
  const { canvas, context: g } = offscreen(across * 2 + 8, down * 2 + 8, scale);
  const cx = across + 4;
  const cy = down + 4;
  g.save();
  g.translate(cx, cy);
  g.scale(1, down / across);
  const film = g.createRadialGradient(0, 0, 0, 0, 0, across);
  film.addColorStop(0, 'rgba(220,240,255,.05)');
  film.addColorStop(0.7, 'rgba(220,240,255,.05)');
  film.addColorStop(1, 'rgba(165,215,255,.4)');
  g.fillStyle = film;
  g.beginPath();
  g.arc(0, 0, across, 0, Math.PI * 2);
  g.fill();
  g.restore();

  const body = () => { g.beginPath(); g.ellipse(cx, cy, across, down, 0, 0, Math.PI * 2); };
  const sheen = g.createLinearGradient(4, 4, 4 + across * 2, 4 + down * 2);
  sheen.addColorStop(0, 'rgba(255,255,255,0)');
  sheen.addColorStop(1, 'rgba(255,150,215,.24)');
  g.fillStyle = sheen;
  body();
  g.fill();

  const rainbow = g.createLinearGradient(4, 4, 4 + across * 2, 4 + down * 2);
  rainbow.addColorStop(0, 'rgba(255,140,200,.84)');
  rainbow.addColorStop(0.35, 'rgba(255,230,120,.84)');
  rainbow.addColorStop(0.7, 'rgba(120,230,255,.84)');
  rainbow.addColorStop(1, 'rgba(185,140,255,.84)');
  g.strokeStyle = rainbow;
  g.lineWidth = 2.6;
  body();
  g.stroke();
  g.strokeStyle = 'rgba(255,255,255,.27)';
  g.lineWidth = 1.2;
  g.beginPath();
  g.ellipse(cx, cy, across - 4, down - 4, 0, 0, Math.PI * 2);
  g.stroke();

  // A window-shaped shine at the top left, a dot under it, and a thin glint
  // along the bottom right.
  g.fillStyle = 'rgba(255,255,255,.75)';
  g.beginPath();
  g.ellipse(4 + across * 0.55, 4 + down * 0.42, across * 0.24, down * 0.09, -38 * Math.PI / 180, 0, Math.PI * 2);
  g.fill();
  const dot = Math.max(3, across * 0.09);
  g.fillStyle = 'rgba(255,255,255,.8)';
  g.beginPath();
  g.arc(4 + across * 0.3 + dot / 2, 4 + down * 0.66 + dot / 2, dot / 2, 0, Math.PI * 2);
  g.fill();
  g.strokeStyle = 'rgba(255,255,255,.47)';
  g.lineWidth = 2.5;
  g.lineCap = 'round';
  g.beginPath();
  g.ellipse(cx, cy, across - 6, down - 6, 0, 15 * Math.PI / 180, 65 * Math.PI / 180);
  g.stroke();

  sprite = canvas;
  bubbleCache.set(id, sprite);
  return sprite;
}

function drawYarn(g, x, y, radius, spin) {
  g.save();
  g.translate(x, y);
  g.rotate(spin * Math.PI / 180);
  g.fillStyle = '#fa80aa';
  g.beginPath();
  g.arc(0, 0, radius, 0, Math.PI * 2);
  g.fill();
  g.strokeStyle = '#ffc8de';
  g.lineWidth = radius * 0.16;
  g.lineCap = 'round';
  for (let index = -1; index <= 1; index++) {
    g.beginPath();
    g.ellipse(index * radius * 0.35, 0, radius * 0.45, radius * 0.92, 0.5, 0, Math.PI * 2);
    g.stroke();
  }
  g.restore();
}

function heartPath(g, x, y, size) {
  g.beginPath();
  g.moveTo(x + size / 2, y + size * 0.92);
  g.bezierCurveTo(x - size * 0.1, y + size * 0.5, x + size * 0.05, y - size * 0.05, x + size / 2, y + size * 0.25);
  g.bezierCurveTo(x + size * 0.95, y - size * 0.05, x + size * 1.1, y + size * 0.5, x + size / 2, y + size * 0.92);
  g.closePath();
}

// The cat pictures, made once from the same SVG as the rest of the site.
const catImages = new Map();

function catImage(mood, tail) {
  const id = `${mood}${tail}`;
  if (!catImages.has(id)) {
    const image = new Image();
    image.src = 'data:image/svg+xml;charset=utf-8,' + encodeURIComponent(
      mochiCat({ coat: 'white', mood, tail }).replace('<svg ', '<svg xmlns="http://www.w3.org/2000/svg" width="208" height="200" '));
    catImages.set(id, image);
  }
  return catImages.get(id);
}

// Shows the game in `view`. `app` gives the words being studied, the synced
// progress and settings, and how to change them. Returns a function that stops
// the game when the learner leaves the page.
export function showGame(view, app) {
  const settings = () => {
    const p = app.progress();
    return {
      fallSeconds: p.fallSeconds ?? DEFAULT_FALL_SECONDS,
      textScale: p.textScale ?? 100,
      pinyinScale: p.pinyinScale ?? 100
    };
  };
  const words = () => app.studyWords().filter(word => word.Chinese?.trim() && word.Pinyin?.trim());

  view.innerHTML = `
    <div class="toolbar" style="justify-content:space-between">
      <h1>Mèo bắn bong bóng</h1>
      <span class="muted">Gõ chữ Hán hoặc pinyin không dấu · Enter để bắn ngay</span>
    </div>
    <div class="game" id="game">
      <canvas id="sky" aria-label="Trò chơi Mèo bắn bong bóng"></canvas>
      <input id="typing" type="text" lang="zh" autocomplete="off" autocapitalize="off" autocorrect="off" spellcheck="false" enterkeyhint="send" aria-label="Gõ từ trong bong bóng">
      <div class="game-menu" id="menu">
        <h2 id="menu-title"></h2>
        <p id="menu-detail"></p>
        <div class="chips" id="modes">${MODES.map(item => `<button class="pill" type="button" data-mode="${item.id}">${item.name}</button>`).join('')}</div>
        <button class="primary" id="start" type="button">▶ Bắt đầu chơi</button>
      </div>
    </div>
    <details class="card game-settings">
      <summary>Cài đặt trò chơi</summary>
      <div class="settings-grid">
        <label>Thời gian rơi (giây)<input id="fall" type="number" min="2" max="60"></label>
        <label>Cỡ chữ (%)<input id="text-scale" type="number" min="50" max="200" step="10"></label>
        <label>Cỡ pinyin (%)<input id="pinyin-scale" type="number" min="50" max="200" step="10"></label>
      </div>
      <p class="muted">Cài đặt được đồng bộ với app trên máy tính.</p>
    </details>`;

  const host = view.querySelector('#game');
  const canvas = view.querySelector('#sky');
  const g = canvas.getContext('2d');
  const input = view.querySelector('#typing');
  const menu = view.querySelector('#menu');
  const startButton = view.querySelector('#start');

  let width = 0;
  let height = 0;
  let scale = 1;
  let frame = 0;
  let lastTime = 0;
  let twinkle = 0;
  let stars = [];
  let snow = [];

  let fallingWords = [];
  let particles = [];
  let floats = [];
  let explosions = [];
  let balls = [];
  let targets = [];
  let running = false;
  let gameOver = false;
  let score = 0;
  let combo = 0;
  let lives = MAX_LIVES;
  let hits = 0;
  let spawnIn = 0;
  let flash = 0;
  let bloodMoon = 0;
  let composing = false;

  let autoHitTimer = 0;
  let prefixWaited = false;
  let prefixSince = 0;

  const cat = { x: 200, wanderTo: 200, facing: 1, running: false, runPhase: 0, throwPose: 0, hop: 0, hopSpeed: 0, squash: 0, nextIdleHop: 0, love: 0, yarn: { x: 200, y: 0 } };

  const level = () => 1 + Math.floor(hits / HITS_PER_LEVEL);
  const typed = () => input.value.trim();
  const roofTop = () => height - ROOF_HEIGHT;
  // Phones get slightly smaller words so a few fit side by side.
  const sizeFactor = () => settings().textScale / 100 * (width < 520 ? 0.85 : 1);

  function fonts() {
    const size = sizeFactor();
    const pinyin = settings().pinyinScale / 100;
    return {
      hanzi: { font: `700 ${29 * size}px ${HANZI_FONT}`, line: 29 * size * 1.35 },
      under: { font: `400 ${14.7 * size * pinyin}px ${UI_FONT}`, line: 14.7 * size * pinyin * 1.4 },
      only: { font: `600 ${22.7 * size * pinyin}px ${UI_FONT}`, line: 22.7 * size * pinyin * 1.35 }
    };
  }

  // ---------- size ----------

  function resize() {
    const box = canvas.getBoundingClientRect();
    if (box.width === 0) return;
    width = box.width;
    height = box.height;
    scale = Math.min(2, window.devicePixelRatio || 1);
    canvas.width = Math.round(width * scale);
    canvas.height = Math.round(height * scale);
    // A fixed scatter of stars for this size, and snow already falling.
    let seed = 7;
    const random = () => (seed = (seed * 16807) % 2147483647) / 2147483647;
    stars = Array.from({ length: Math.round(70 * Math.min(1, width / 700)) + 20 }, () => ({ x: random() * width, y: random() * (height - ROOF_HEIGHT - 20) }));
    snow = Array.from({ length: Math.round(90 * Math.min(1, width / 800)) + 20 }, () => ({
      x: random() * width, y: random() * height, size: 3 + random() * 4, phase: random() * Math.PI * 2
    }));
    if (!running) {
      cat.x = cat.wanderTo = width / 2;
    }
    draw();
  }

  // ---------- menu ----------

  function showMenu() {
    menu.hidden = false;
    const best = app.progress().bestGameScore ?? 0;
    const hasWords = words().length > 0;
    view.querySelector('#menu-title').textContent = gameOver ? '😿 Hết lượt rồi!' : '🐱 Mèo bắn bong bóng';
    view.querySelector('#menu-detail').innerHTML = gameOver
      ? `Điểm: <b>${score}</b> · Cao nhất: <b>${best}</b> · Chọn chế độ rồi chơi lại`
      : !hasWords
        ? 'Hãy chọn từ để học trước (<a href="#/words">Danh sách từ</a> → tích chọn từ).'
        : `Gõ chữ Hán hoặc pinyin không dấu của chữ trong bong bóng để mèo ném cuộn len làm vỡ bóng trước khi bóng chạm mái nhà!${best > 0 ? ` Cao nhất: <b>${best}</b>` : ''}`;
    view.querySelectorAll('[data-mode]').forEach(button => button.classList.toggle('on', button.dataset.mode === mode));
    startButton.textContent = gameOver ? '🔁 Chơi lại' : '▶ Bắt đầu chơi';
    startButton.disabled = !hasWords;
  }

  function start() {
    fallingWords = [];
    particles = [];
    explosions = [];
    floats = [];
    targets = [];
    balls = [];
    score = 0;
    combo = 0;
    lives = MAX_LIVES;
    hits = 0;
    bloodMoon = 0;
    flash = 0;
    gameOver = false;
    spawnIn = 0.3;
    cat.x = cat.wanderTo = width / 2;
    cat.hop = cat.hopSpeed = cat.squash = cat.love = 0;
    cat.yarn = { x: cat.x, y: height - ROOF_HEIGHT - 30 };
    menu.hidden = true;
    input.value = '';
    running = true;
    lastTime = performance.now();
    input.focus({ preventScroll: true });
    loop();
  }

  function endGame() {
    flash = 0;
    gameOver = true;
    running = false;
    clearTimeout(autoHitTimer);
    if (score > (app.progress().bestGameScore ?? 0)) {
      app.changeProgress(p => { p.bestGameScore = Math.max(p.bestGameScore ?? 0, score); });
    }
    input.blur();
    showMenu();
    draw();
  }

  // ---------- typing ----------

  function restartAutoHit() {
    clearTimeout(autoHitTimer);
    prefixWaited = false;
    autoHitTimer = setTimeout(() => tryHit(false), AUTO_HIT_DELAY);
  }

  // Empties the typing and throws away what the input method is composing, so a
  // shot word's pinyin does not turn into characters afterwards.
  function clearTyping() {
    clearTimeout(autoHitTimer);
    input.value = '';
    if (composing && !matchMedia('(pointer: coarse)').matches) {
      // Losing focus ends the composition; what it commits is cleared again.
      clearing = true;
      input.blur();
      input.value = '';
      clearing = false;
      input.focus({ preventScroll: true });
    }
  }
  let clearing = false;

  function floatText(text, x, y, color) {
    floats.push({ text, x, y, color, life: 1.2 });
  }

  // A wrong key wipes the typing: as soon as it cannot lead to any falling
  // word, it is cleared, the combo breaks, and the learner starts again.
  function checkTyping() {
    const text = typed();
    const open = fallingWords.filter(falling => !falling.targeted);
    if (!running || text.length === 0 || open.length === 0) return;
    const characters = [...text].filter(isHanzi).join('');
    const letters = pinyinKey([...text].filter(character => !isHanzi(character)).join(''));
    if (open.some(falling => fits(falling.word, characters, letters))) return;
    combo = 0;
    const wrong = 'Sai rồi! Gõ lại từ đầu ✋';
    floats = floats.filter(item => item.text !== wrong);
    floatText(wrong, width / 2, height - 90, '#fca5a5');
    clearTyping();
  }

  function tryHit(fromEnter) {
    if (!running) return;
    const text = typed();
    const key = pinyinKey(text);
    if (text.length === 0) return;
    const open = fallingWords.filter(falling => !falling.targeted);

    // When a longer falling word starts the same way, give the learner a moment
    // longer to finish it before the shorter one is shot.
    if (!fromEnter && !prefixWaited &&
        open.some(falling => isAnswer(falling, text, key)) &&
        open.some(falling => couldBecome(falling, text, key))) {
      prefixWaited = true;
      prefixSince = performance.now();
      autoHitTimer = setTimeout(() => tryHit(false), PREFIX_HIT_DELAY);
      return;
    }

    const target = open.filter(falling => isAnswer(falling, text, key)).sort((a, b) => b.y - a.y)[0];
    if (!target) {
      if (fromEnter) {
        combo = 0;
        floatText('Chưa đúng 😅', width / 2, height - 70, '#fca5a5');
        clearTyping();
      }
      return;
    }
    target.targeted = true;
    targets.push(target);
    clearTyping();
  }

  input.addEventListener('input', () => {
    if (clearing) return;
    restartAutoHit();
    checkTyping();
  });
  input.addEventListener('compositionstart', () => { composing = true; });
  input.addEventListener('compositionend', () => { composing = false; });
  input.addEventListener('keydown', event => {
    if (event.key === 'Enter' && !event.isComposing) {
      event.preventDefault();
      clearTimeout(autoHitTimer);
      tryHit(true);
    }
  });
  // Tapping the sky puts the keyboard back on the typing box.
  canvas.addEventListener('pointerdown', () => { if (running) input.focus({ preventScroll: true }); });

  // ---------- words ----------

  function measure(word) {
    const f = fonts();
    g.font = f.hanzi.font;
    const hanzi = g.measureText(word.Chinese).width;
    if (mode === 'hanzi') return { width: hanzi, height: f.hanzi.line };
    if (mode === 'both') {
      g.font = f.under.font;
      return { width: Math.max(hanzi, g.measureText(word.Pinyin).width), height: f.hanzi.line + f.under.line };
    }
    g.font = f.only.font;
    return { width: g.measureText(word.Pinyin).width, height: f.only.line };
  }

  // A word not already on screen, picked at random.
  function pickWord() {
    const list = words();
    if (list.length === 0) return null;
    const flying = new Set(fallingWords.map(falling => falling.word.Chinese));
    const free = list.filter(word => !flying.has(word.Chinese));
    const pool = free.length > 0 ? free : list;
    return pool[Math.floor(Math.random() * pool.length)];
  }

  // Words all fall at one speed, so a new bubble only collides with ones still
  // near the top: of a few random spots, take the one farthest from those.
  function pickSpawnX(size) {
    const radii = bubbleRadii(size);
    const margin = Math.min(70, Math.max(radii.across - size.width / 2 + 6, (width - size.width) / 2));
    const range = Math.max(1, width - size.width - margin * 2);
    const nearTop = fallingWords.filter(falling => falling.y < 280).map(falling => falling.x + falling.size.width / 2);
    let best = margin + Math.random() * range;
    let bestGap = -1;
    for (let attempt = 0; attempt < 10 && nearTop.length > 0; attempt++) {
      const x = margin + Math.random() * range;
      const gap = Math.min(...nearTop.map(centre => Math.abs(centre - (x + size.width / 2))));
      if (gap > bestGap) {
        best = x;
        bestGap = gap;
      }
    }
    return best;
  }

  // A transparent picture of the word: each line centred, with a dark outline
  // so it reads on the bubble, and its typed start redrawn in red.
  function renderWord(falling, hanziTyped, pinyinTyped) {
    const f = falling.fonts;
    const { canvas: image, context: w } = offscreen(falling.size.width + WORD_PAD * 2, falling.size.height + WORD_PAD * 2, scale);
    const color = falling.targeted ? '#facc15' : falling.color;
    w.textBaseline = 'middle';
    w.lineJoin = 'round';
    const line = (text, font, top, lineHeight, typedLength) => {
      w.font = font;
      const left = WORD_PAD + (falling.size.width - w.measureText(text).width) / 2;
      const y = WORD_PAD + top + lineHeight / 2;
      w.strokeStyle = '#1c0e08';
      w.lineWidth = 4;
      w.strokeText(text, left, y);
      w.fillStyle = color;
      w.fillText(text, left, y);
      if (typedLength > 0) {
        w.fillStyle = TYPED_COLOR;
        w.fillText(text.slice(0, typedLength), left, y);
      }
    };
    if (falling.mode === 'hanzi') {
      line(falling.word.Chinese, f.hanzi.font, 0, falling.size.height, hanziTyped);
    } else if (falling.mode === 'both') {
      line(falling.word.Chinese, f.hanzi.font, 0, f.hanzi.line, hanziTyped);
      line(falling.word.Pinyin, f.under.font, f.hanzi.line, falling.size.height - f.hanzi.line, pinyinTyped);
    } else {
      line(falling.word.Pinyin, f.only.font, 0, falling.size.height, pinyinTyped);
    }
    return image;
  }

  // ---------- effects ----------

  // A bubble bursting: soapy droplets spray from its rim and tiny bubbles
  // float off. One that reached the roof splashes upwards instead.
  function pop(x, y, radii, ground) {
    explosions.push({ x, y, size: Math.max(radii.across, radii.down), ground, age: 0 });
    for (let index = 0; index < (ground ? 22 : 20); index++) {
      const angle = ground ? Math.PI + Math.random() * Math.PI : Math.random() * Math.PI * 2;
      const power = 90 + Math.random() * 220;
      particles.push({
        x: ground ? x + Math.cos(angle) * radii.across * 0.6 : x + Math.cos(angle) * radii.across,
        y: ground ? y : y + Math.sin(angle) * radii.down,
        vx: Math.cos(angle) * power,
        vy: Math.sin(angle) * power - 40,
        color: FOAM_COLORS[Math.floor(Math.random() * FOAM_COLORS.length)],
        life: 0.7,
        bubble: false,
        size: 5
      });
    }
    for (let index = 0; index < 6; index++) {
      const angle = Math.random() * Math.PI * 2;
      particles.push({
        x: x + Math.cos(angle) * radii.across * 0.5,
        y: y + Math.sin(angle) * radii.down * 0.5,
        vx: Math.cos(angle) * 50,
        vy: -30 - Math.random() * 50,
        color: '#fff',
        life: 1.3,
        bubble: true,
        size: 5 + Math.random() * 7
      });
    }
  }

  // A ball reached its bubble: pop, points, and the word counts as right.
  function hitWord(target) {
    fallingWords = fallingWords.filter(falling => falling !== target);
    combo++;
    hits++;
    const points = 10 * combo;
    score += points;
    const cx = target.x + target.size.width / 2;
    const cy = target.y + target.size.height / 2;
    pop(cx, cy, bubbleRadii(target.size), false);
    cat.love = 0.9;
    floatText(combo > 1 ? `+${points}  ✨x${combo}` : `+${points}`, cx, cy, '#fde047');
    if (hits % HITS_PER_LEVEL === 0) {
      floatText(`⭐ Lên cấp ${level()}!`, width / 2, height / 2, '#fb923c');
    }
    app.record(target.word, true);
  }

  // ---------- each frame ----------

  function step(dt) {
    twinkle += dt;
    const roof = roofTop();
    const currentLevel = level();
    const fall = settings().fallSeconds;

    // New words drop in from above the sky at a random spot.
    spawnIn -= dt;
    if (spawnIn <= 0 && fallingWords.length < Math.min(7, 3 + currentLevel)) {
      const word = pickWord();
      if (word) {
        const size = measure(word);
        fallingWords.push({
          word, key: pinyinKey(word.Pinyin), size, mode, fonts: fonts(),
          x: pickSpawnX(size), y: -size.height / 2 - bubbleRadii(size).down - 2,
          color: TEXT_COLORS[Math.floor(Math.random() * TEXT_COLORS.length)],
          targeted: false, image: null, imageKey: ''
        });
        spawnIn = Math.max(0.8, 2.4 - currentLevel * 0.22) * fall / DEFAULT_FALL_SECONDS;
      }
    }

    // At level 1 a word takes fallSeconds to reach the roof on any screen size;
    // each level after that speeds it up a little.
    const speed = roof / fall * (34 + currentLevel * 10) / 44;
    for (const falling of [...fallingWords]) {
      falling.y += speed * dt;
      const radii = bubbleRadii(falling.size);
      const bottom = falling.y + falling.size.height / 2 + radii.down;
      if (bottom < roof) continue;
      if (falling.targeted) {
        // Answered, but the ball is still on its way: it counts as a hit.
        hitWord(falling);
        continue;
      }
      fallingWords = fallingWords.filter(item => item !== falling);
      pop(falling.x + falling.size.width / 2, roof, radii, true);
      lives--;
      combo = 0;
      flash = 0.35;
      app.record(falling.word, false);
      if (lives <= 0) {
        endGame();
        return;
      }
    }

    updateCat(dt);
    updateHops(dt);
    updateBalls(dt);
    for (const particle of particles) {
      particle.x += particle.vx * dt;
      particle.y += particle.vy * dt;
      particle.vy += (particle.bubble ? -20 : 260) * dt;
      particle.life -= dt;
    }
    particles = particles.filter(particle => particle.life > 0);
    for (const blast of explosions) blast.age += dt;
    explosions = explosions.filter(blast => blast.age < 0.45);
    for (const text of floats) {
      text.y -= 40 * dt;
      text.life -= dt;
    }
    floats = floats.filter(text => text.life > 0);
    flash = Math.max(0, flash - dt);

    // The moon slowly reddens as hearts run out: a hint at 2, a blood moon at 1.
    const moonTarget = lives >= MAX_LIVES ? 0 : lives === 2 ? 0.45 : 1;
    bloodMoon += Math.max(-dt * 0.5, Math.min(dt * 0.5, moonTarget - bloodMoon));
  }

  function updateSnow(dt) {
    for (const flake of snow) {
      flake.phase += dt * 1.4;
      flake.y += (12 + flake.size * 8) * dt;
      flake.x += Math.sin(flake.phase) * 14 * dt + 6 * dt;
      if (flake.y > height) {
        flake.y = -flake.size;
        flake.x = Math.random() * width;
      } else if (flake.x > width + 4) {
        flake.x = -4;
      }
    }
  }

  // The cat throws at each marked word as soon as it is marked; between throws
  // it pads about the roof.
  function updateCat(dt) {
    cat.throwPose = Math.max(0, cat.throwPose - dt);
    while (targets.length > 0) {
      const aiming = targets.shift();
      if (!fallingWords.includes(aiming)) continue;
      const centre = aiming.x + aiming.size.width / 2;
      cat.facing = Math.sign(centre - cat.x) || cat.facing;
      balls.push({ x: cat.yarn.x, y: cat.yarn.y, startX: cat.yarn.x, startY: cat.yarn.y, dx: 0, dy: -1, spin: 0, target: aiming });
      cat.hopSpeed = Math.max(cat.hopSpeed, 340);
      cat.wanderTo = Math.min(Math.max(centre, 40), Math.max(40, width - 40));
      cat.throwPose = 0.3;
    }
    if (Math.abs(cat.wanderTo - cat.x) < 4) {
      cat.wanderTo = 40 + Math.random() * Math.max(1, width - 80);
    }
    const speed = cat.throwPose > 0 ? 0 : 160;
    const distance = cat.wanderTo - cat.x;
    cat.running = speed > 0 && Math.abs(distance) > 2;
    if (cat.running) {
      cat.facing = Math.sign(distance);
      cat.x += Math.max(-speed * dt, Math.min(speed * dt, distance));
      cat.runPhase += dt * 11;
    }
  }

  // Little hops while it runs, a bigger jump to throw, the odd hop while it
  // waits, and a squash each time it lands.
  function updateHops(dt) {
    if (cat.hop <= 0 && cat.hopSpeed <= 0) {
      if (cat.running) {
        cat.hopSpeed = 210;
      } else if (twinkle >= cat.nextIdleHop) {
        cat.hopSpeed = 190;
        cat.nextIdleHop = twinkle + 1.8 + Math.random() * 2.5;
      }
    }
    cat.hopSpeed -= 1500 * dt;
    cat.hop += cat.hopSpeed * dt;
    if (cat.hop <= 0) {
      if (cat.hopSpeed < -80) cat.squash = 1;
      cat.hop = 0;
      cat.hopSpeed = 0;
    }
    cat.squash = Math.max(0, cat.squash - dt * 5);
    cat.love = Math.max(0, cat.love - dt);
  }

  // Balls of yarn spin and home in on their bubble; one that lost its bubble
  // flies off the top.
  function updateBalls(dt) {
    const speed = 1300;
    for (const ball of [...balls]) {
      ball.spin += dt * 720;
      const alive = fallingWords.includes(ball.target);
      const dx = alive ? ball.target.x + ball.target.size.width / 2 - ball.x : 0;
      const dy = alive ? ball.target.y + ball.target.size.height / 2 - ball.y : -1;
      const length = Math.max(0.001, Math.hypot(dx, dy));
      if (alive && length < hitRadius(ball.target)) {
        balls = balls.filter(item => item !== ball);
        hitWord(ball.target);
        continue;
      }
      ball.dx = dx / length;
      ball.dy = dy / length;
      ball.x += ball.dx * speed * dt;
      ball.y += ball.dy * speed * dt;
      if (ball.y < -40) balls = balls.filter(item => item !== ball);
    }
  }

  // ---------- drawing ----------

  function draw() {
    if (width === 0) return;
    g.setTransform(scale, 0, 0, scale, 0, 0);
    const blood = bloodMoon;

    // Night sky: deep blue, turning crimson under a blood moon.
    const sky = g.createLinearGradient(0, 0, 0, height);
    sky.addColorStop(0, rgb(mix([15, 23, 42], [42, 6, 10], blood)));
    sky.addColorStop(1, rgb(mix([49, 46, 129], [127, 21, 35], blood)));
    g.fillStyle = sky;
    g.fillRect(0, 0, width, height);

    stars.forEach((star, index) => {
      g.fillStyle = `rgba(255,255,255,${Math.min(1, (120 + 100 * Math.abs(Math.sin(twinkle * 1.5 + index))) / 255)})`;
      const size = index % 5 === 0 ? 3 : 2;
      g.fillRect(star.x, star.y, size, size);
    });

    drawMoon(blood);

    // Soft flakes: a faint glow around a brighter core.
    g.fillStyle = 'rgba(241,245,249,.18)';
    g.beginPath();
    for (const flake of snow) {
      g.moveTo(flake.x + flake.size * 1.5, flake.y + flake.size / 2);
      g.arc(flake.x + flake.size / 2, flake.y + flake.size / 2, flake.size, 0, Math.PI * 2);
    }
    g.fill();
    g.fillStyle = 'rgba(241,245,249,.88)';
    g.beginPath();
    for (const flake of snow) {
      g.moveTo(flake.x + flake.size, flake.y + flake.size / 2);
      g.arc(flake.x + flake.size / 2, flake.y + flake.size / 2, flake.size / 2, 0, Math.PI * 2);
    }
    g.fill();

    const text = running ? typed() : '';
    for (const falling of fallingWords) drawBubble(falling);
    for (const falling of fallingWords) drawWord(falling, text);
    for (const ball of balls) drawBall(ball);
    drawRoof();
    drawCat();
    for (const blast of explosions) drawPop(blast);

    for (const particle of particles) {
      const alpha = Math.min(1, Math.max(0, particle.life / 0.7));
      if (particle.bubble) {
        g.strokeStyle = `rgba(220,240,255,${alpha * 0.75})`;
        g.lineWidth = 1.4;
        g.beginPath();
        g.arc(particle.x, particle.y, particle.size / 2, 0, Math.PI * 2);
        g.stroke();
        g.fillStyle = `rgba(255,255,255,${alpha})`;
        g.fillRect(particle.x - particle.size / 4, particle.y - particle.size / 4, 2, 2);
      } else {
        g.globalAlpha = alpha;
        g.fillStyle = particle.color;
        g.beginPath();
        g.arc(particle.x, particle.y, 2.5, 0, Math.PI * 2);
        g.fill();
        g.globalAlpha = 1;
      }
    }

    g.font = `600 17px ${UI_FONT}`;
    g.textAlign = 'center';
    g.textBaseline = 'middle';
    for (const item of floats) {
      g.globalAlpha = Math.min(1, Math.max(0, item.life / 1.2));
      g.fillStyle = item.color;
      g.fillText(item.text, item.x, item.y);
    }
    g.globalAlpha = 1;
    g.textAlign = 'left';

    if (blood > 0) {
      g.fillStyle = `rgba(220,38,38,${0.22 * blood})`;
      g.fillRect(0, 0, width, height);
    }
    if (flash > 0) {
      g.fillStyle = `rgba(239,68,68,${flash / 0.35 * 0.31})`;
      g.fillRect(0, 0, width, height);
    }
    if (!running) {
      g.fillStyle = 'rgba(2,6,23,.59)';
      g.fillRect(0, 0, width, height);
    }
    drawHud();
    if (running) drawTyped(text);
  }

  // A crescent moon below the lives; as hearts run out it fills into a full,
  // red blood moon with a wider glow.
  function drawMoon(blood) {
    const size = 58;
    const x = width - 160 + size / 2;
    const y = (width < 520 ? 70 : 84) + size / 2;
    const color = mix([254, 249, 195], [220, 38, 38], blood);
    g.fillStyle = rgb(color, (22 + 28 * blood) / 255);
    g.beginPath();
    g.arc(x, y, size / 2 + 10 + 8 * blood, 0, Math.PI * 2);
    g.fill();
    g.save();
    g.beginPath();
    g.arc(x, y, size / 2, 0, Math.PI * 2);
    g.clip();
    g.beginPath();
    g.arc(x, y, size / 2, 0, Math.PI * 2);
    g.arc(x - size / 2 + 16 + 60 * blood + 27, y - size / 2 - 8 - 30 * blood + 27, 27, 0, Math.PI * 2);
    g.fillStyle = rgb(color);
    g.fill('evenodd');
    g.restore();
  }

  // A word's bubble, with a sparkle running round its rim. A bubble the ball is
  // on its way to glows pink.
  function drawBubble(falling) {
    const { across, down } = bubbleRadii(falling.size);
    const a = Math.round(across);
    const d = Math.round(down);
    const cx = falling.x + falling.size.width / 2;
    const cy = falling.y + falling.size.height / 2;
    g.drawImage(bubbleSprite(a, d, scale), cx - a - 4, cy - d - 4, a * 2 + 8, d * 2 + 8);
    const start = ((twinkle * 70 + falling.x) % 360) * Math.PI / 180;
    g.strokeStyle = 'rgba(255,255,255,.43)';
    g.lineWidth = 2;
    g.lineCap = 'round';
    g.beginPath();
    g.ellipse(cx, cy, a, d, 0, start, start + 24 * Math.PI / 180);
    g.stroke();
    if (falling.targeted) {
      g.strokeStyle = 'rgba(255,120,200,.67)';
      g.lineWidth = 3;
      g.beginPath();
      g.ellipse(cx, cy, a + 3, d + 3, 0, 0, Math.PI * 2);
      g.stroke();
    }
  }

  // Each word keeps a picture of itself and only redraws it when its red part
  // or its colour changes.
  function drawWord(falling, text) {
    const shown = falling.targeted ? '' : text;
    const hanziTyped = typedHanziLength(falling.word.Chinese, shown);
    const pinyinTyped = typedPinyinLength(falling.word, shown, hanziTyped);
    const key = `${hanziTyped},${pinyinTyped},${falling.targeted}`;
    if (!falling.image || falling.imageKey !== key) {
      falling.image = renderWord(falling, hanziTyped, pinyinTyped);
      falling.imageKey = key;
    }
    g.drawImage(falling.image, falling.x - WORD_PAD, falling.y - WORD_PAD, falling.size.width + WORD_PAD * 2, falling.size.height + WORD_PAD * 2);
  }

  // A thrown ball of yarn with its loose end of thread trailing behind it.
  function drawBall(ball) {
    const length = Math.min(70, Math.hypot(ball.x - ball.startX, ball.y - ball.startY));
    if (length > 4) {
      g.strokeStyle = 'rgba(255,170,205,.86)';
      g.lineWidth = 2;
      g.lineCap = 'round';
      g.beginPath();
      for (let index = 0; index < 9; index++) {
        const along = length * index / 8;
        const wave = Math.sin(index * 1.3 + twinkle * 18) * 4 * index / 8;
        const x = ball.x - ball.dx * along - ball.dy * wave;
        const y = ball.y - ball.dy * along + ball.dx * wave;
        index === 0 ? g.moveTo(x, y) : g.lineTo(x, y);
      }
      g.stroke();
    }
    drawYarn(g, ball.x, ball.y, YARN_RADIUS * 1.15, ball.spin);
  }

  // A dark tiled roof along the bottom of the sky; a word that lands here is lost.
  function drawRoof() {
    const top = roofTop();
    const roof = g.createLinearGradient(0, top - 1, 0, height + 1);
    roof.addColorStop(0, '#475569');
    roof.addColorStop(1, '#1e293b');
    g.fillStyle = roof;
    g.fillRect(0, top, width, ROOF_HEIGHT);
    g.strokeStyle = 'rgba(15,23,42,.4)';
    g.lineWidth = 2;
    g.beginPath();
    g.moveTo(0, top + 2);
    g.lineTo(width, top + 2);
    for (let x = 0; x < width; x += 28) {
      g.moveTo(x + 28, top + 12);
      g.ellipse(x + 14, top + 12, 14, 8, 0, 0, Math.PI);
    }
    g.stroke();
  }

  // The white mochi cat on the roof: it hugs a ball of yarn and hops about,
  // beams after a pop and looks sad for a moment when a bubble lands.
  function drawCat() {
    const feetY = roofTop() + 4;
    const lift = cat.hop;
    g.fillStyle = `rgba(0,0,0,${Math.max(20, 70 - lift) / 255})`;
    const shadow = 50 - Math.min(20, lift * 0.3);
    g.beginPath();
    g.ellipse(cat.x, feetY, shadow / 2, 4, 0, 0, Math.PI * 2);
    g.fill();

    const throwing = cat.throwPose > 0;
    const airborne = lift > 0;
    const sx = 1 + 0.14 * cat.squash - (airborne ? 0.04 : 0);
    const sy = 1 - 0.16 * cat.squash + (airborne ? 0.06 : 0);
    const mood = throwing || cat.love > 0 ? 'happy' : flash > 0 ? 'sad' : 'smile';
    const image = catImage(mood, -cat.facing >= 0 ? 1 : -1);
    const lean = cat.running ? cat.facing * 8 * Math.PI / 180 : 0;
    const step = cat.running ? Math.sin(cat.runPhase) * 2 : 0;
    g.save();
    g.translate(cat.x, feetY - lift - Math.abs(step));
    g.rotate(lean);
    g.scale(sx, sy);
    if (image.complete && image.naturalWidth > 0) {
      g.drawImage(image, -CAT_SIZE / 2 * 1.04, -CAT_SIZE, CAT_SIZE * 1.04, CAT_SIZE);
    }
    g.restore();

    // Yarn held at its belly, or flung up from a raised paw while throwing.
    const bellyX = cat.x;
    const bellyY = feetY - lift - CAT_SIZE * 0.28;
    if (!throwing) {
      drawYarn(g, bellyX, bellyY, YARN_RADIUS, twinkle * 40);
      cat.yarn = { x: bellyX, y: bellyY };
    } else {
      g.fillStyle = '#fffdfa';
      g.strokeStyle = '#70605c';
      g.lineWidth = 2.4;
      g.beginPath();
      g.ellipse(cat.x + cat.facing * CAT_SIZE * 0.36, feetY - lift - CAT_SIZE * 0.78, 7, 6, 0, 0, Math.PI * 2);
      g.fill();
      g.stroke();
    }

    if (cat.love > 0) {
      const rise = 1 - cat.love / 0.9;
      g.fillStyle = `rgba(240,92,110,${1 - rise * rise})`;
      heartPath(g, cat.x + 22 + rise * 6, feetY - lift - CAT_SIZE - 6 - rise * 22, 13);
      g.fill();
      heartPath(g, cat.x - 34 - rise * 4, feetY - lift - CAT_SIZE + 4 - rise * 16, 10);
      g.fill();
    }
  }

  // The film tears into curling rainbow shreds after a quick white flash. On
  // the roof it leaves a flat ripple.
  function drawPop(blast) {
    const t = blast.age / 0.45;
    const grow = 1 - (1 - t) * (1 - t);
    const fade = 1 - t;
    g.lineCap = 'round';
    if (blast.ground) {
      const spread = blast.size * (0.6 + 1.2 * grow);
      g.strokeStyle = `rgba(200,238,255,${0.78 * fade})`;
      g.lineWidth = 3 * fade + 1;
      g.beginPath();
      g.ellipse(blast.x, blast.y, spread, spread * 0.18, 0, 0, Math.PI * 2);
      g.stroke();
      return;
    }
    if (t < 0.2) {
      g.strokeStyle = `rgba(255,255,255,${0.86 * (1 - t / 0.2)})`;
      g.lineWidth = 3;
      g.beginPath();
      g.arc(blast.x, blast.y, blast.size * (0.9 + 0.3 * t), 0, Math.PI * 2);
      g.stroke();
    }
    const radius = blast.size * (1 + 0.6 * grow);
    g.globalAlpha = 0.9 * fade;
    g.lineWidth = 2.6 * fade + 0.6;
    for (let index = 0; index < 12; index++) {
      const start = (index * 30 + Math.sin(index * 7.3) * 12 + t * 40) * Math.PI / 180;
      const curl = radius * (0.92 + 0.12 * (index % 3));
      g.strokeStyle = FOAM_COLORS[index % (FOAM_COLORS.length - 1)];
      g.beginPath();
      g.arc(blast.x, blast.y, curl, start, start + (16 * fade + 2) * Math.PI / 180);
      g.stroke();
    }
    g.globalAlpha = 1;
  }

  // Score and combo on the left, level in the middle, lives on the right.
  function pill(x, y, icon, text, ink) {
    g.font = `600 16px ${UI_FONT}`;
    const pillWidth = 38 + g.measureText(text).width + 16;
    g.fillStyle = 'rgba(255,255,255,.24)';
    g.beginPath();
    g.roundRect(x, y, pillWidth, 34, 17);
    g.fill();
    g.textBaseline = 'middle';
    g.fillStyle = ink;
    g.font = `16px "Segoe UI Emoji", "Apple Color Emoji", "Noto Color Emoji", sans-serif`;
    g.fillText(icon, x + 11, y + 18);
    g.font = `600 16px ${UI_FONT}`;
    g.fillText(text, x + 38, y + 18);
    return pillWidth;
  }

  function drawHud() {
    const narrow = width < 520;
    const scoreWidth = pill(12, 12, '⭐', `${score} điểm`, '#fde047');
    if (combo > 1) {
      pill(narrow ? 12 : 12 + scoreWidth + 10, narrow ? 52 : 12, '✨', `Combo x${combo}`, '#fdba74');
    }
    const levelText = `Cấp ${level()}`;
    g.font = `600 16px ${UI_FONT}`;
    const levelWidth = 38 + g.measureText(levelText).width + 16;
    const levelX = narrow ? Math.max(12 + scoreWidth + 8, width - 12 - 3 * 30 - 8 - levelWidth) : width / 2 - levelWidth / 2;
    pill(levelX, 12, '🐾', levelText, '#fff');
    for (let index = 0; index < MAX_LIVES; index++) {
      const size = narrow ? 22 : 24;
      g.fillStyle = index < lives ? '#f43f5e' : '#475569';
      heartPath(g, width - 14 - (MAX_LIVES - index) * (size + 6), 18, size);
      g.fill();
    }
  }

  // What is being typed, in a faint pill just above the roof.
  function drawTyped(text) {
    if (!text) return;
    g.font = `700 19px ${HANZI_FONT}`;
    const textWidth = g.measureText(text).width;
    const boxWidth = textWidth + 36;
    const boxHeight = 38;
    const x = (width - boxWidth) / 2;
    const y = roofTop() - boxHeight - CAT_SIZE - 6;
    g.fillStyle = 'rgba(2,6,23,.43)';
    g.beginPath();
    g.roundRect(x, y, boxWidth, boxHeight, boxHeight / 2);
    g.fill();
    g.fillStyle = '#e2e8f0';
    g.textAlign = 'center';
    g.textBaseline = 'middle';
    g.fillText(text, width / 2, y + boxHeight / 2 + 1);
    g.textAlign = 'left';
    // While a shorter word waits in case a longer one is being typed, a bar
    // under the text runs down to the moment it is thrown at.
    if (prefixWaited) {
      const left = Math.min(1, Math.max(0, 1 - (performance.now() - prefixSince) / PREFIX_HIT_DELAY));
      g.fillStyle = 'rgba(255,255,255,.27)';
      g.fillRect(x + 12, y + boxHeight + 4, boxWidth - 24, 3);
      g.fillStyle = 'rgba(250,128,170,.86)';
      g.fillRect(x + 12, y + boxHeight + 4, (boxWidth - 24) * left, 3);
    }
  }

  // ---------- loop ----------

  let stopped = false;

  function loop() {
    cancelAnimationFrame(frame);
    const tick = now => {
      if (stopped) return;
      const dt = Math.min(0.05, Math.max(0, (now - lastTime) / 1000));
      lastTime = now;
      if (running && height >= 120) {
        step(dt);
      } else {
        twinkle += dt;
      }
      updateSnow(dt);
      draw();
      frame = requestAnimationFrame(tick);
    };
    lastTime = performance.now();
    frame = requestAnimationFrame(tick);
  }

  // Pause while the tab is hidden; carry on where it was when shown again.
  const onVisibility = () => {
    if (document.visibilityState === 'hidden') {
      cancelAnimationFrame(frame);
    } else if (!stopped) {
      loop();
    }
  };

  // ---------- settings ----------

  const fallInput = view.querySelector('#fall');
  const textInput = view.querySelector('#text-scale');
  const pinyinInput = view.querySelector('#pinyin-scale');
  const showSettings = () => {
    const current = settings();
    fallInput.value = current.fallSeconds;
    textInput.value = current.textScale;
    pinyinInput.value = current.pinyinScale;
  };
  const setting = (element, key, min, max, fallback) => element.addEventListener('change', () => {
    const value = Math.min(max, Math.max(min, Math.round(Number(element.value) || fallback)));
    element.value = value;
    app.changeProgress(p => { p[key] = value; });
  });
  setting(fallInput, 'fallSeconds', 2, 60, DEFAULT_FALL_SECONDS);
  setting(textInput, 'textScale', 50, 200, 100);
  setting(pinyinInput, 'pinyinScale', 50, 200, 100);
  showSettings();

  view.querySelector('#modes').addEventListener('click', event => {
    const chosen = event.target.dataset?.mode;
    if (!chosen) return;
    mode = chosen;
    showMenu();
  });
  startButton.addEventListener('click', start);

  const observer = new ResizeObserver(resize);
  observer.observe(host);
  document.addEventListener('visibilitychange', onVisibility);
  resize();
  showMenu();
  loop();

  return {
    // A sync brought in new progress: new settings and best score show at once.
    refresh() {
      showSettings();
      if (!running) showMenu();
    },
    stop() {
      stopped = true;
      running = false;
      clearTimeout(autoHitTimer);
      cancelAnimationFrame(frame);
      observer.disconnect();
      document.removeEventListener('visibilitychange', onVisibility);
    }
  };
}
