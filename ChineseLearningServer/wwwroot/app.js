// The study website: sign-in, Home, word list, Flashcard and typing practice.
// Progress lives in this browser (so it works offline) and is synced with the
// server, where the desktop app syncs too.
import { SyncClient, emptyProgress, normalize } from './sync.js';
import { mochiCat } from './cats.js';
import { showGame } from './game.js';
import { showTranslate } from './translate.js';

const HSK_LEVELS = ['HSK 1', 'HSK 2', 'HSK 3', 'HSK 4', 'HSK 5', 'HSK 6', 'HSK 7-9'];
const PAGE_SIZE = 50;
const DEFAULT_PRACTICE_SECONDS = 15;

const view = document.getElementById('view');
const state = {
  token: localStorage.getItem('cl.token'),
  userName: localStorage.getItem('cl.user') ?? '',
  progress: normalize(JSON.parse(localStorage.getItem('cl.progress') ?? 'null')),
  words: null,
  byChinese: new Map(),
  sync: null
};

// ---------- helpers ----------

function escape(text) {
  return String(text ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);
}

function html(strings, ...values) {
  return strings.reduce((out, part, i) => out + part + (i < values.length ? values[i] : ''), '');
}

// Same rules as Form1.NormalizePinyin: no tone marks, spaces, apostrophes or
// hyphens, lower case, v as u.
function normalizePinyin(text) {
  return String(text ?? '').normalize('NFD').replace(/[̀-ͯ\s'’-]/g, '').toLowerCase().replace(/v/g, 'u');
}

// Same as Form1.NormalizePracticeAnswer: ignore spaces, punctuation and symbols.
function normalizeAnswer(text) {
  return String(text ?? '').replace(/[\s\p{P}\p{S}]/gu, '');
}

function shuffle(list) {
  const copy = [...list];
  for (let i = copy.length - 1; i > 0; i--) {
    const j = Math.floor(Math.random() * (i + 1));
    [copy[i], copy[j]] = [copy[j], copy[i]];
  }
  return copy;
}

function badge(color, coat, mood) {
  return `<div class="badge breathe" style="background:${color}">${mochiCat({ coat, mood })}</div>`;
}

// Every change to progress goes through here: saved in the browser at once,
// synced a few seconds after the last change.
function changeProgress(update) {
  update(state.progress);
  saveLocal();
  state.sync?.schedule();
}

function saveLocal() {
  localStorage.setItem('cl.progress', JSON.stringify(state.progress));
}

function studyWords() {
  return state.progress.selectedWords.map(chinese => state.byChinese.get(chinese)).filter(Boolean);
}

async function loadWords() {
  if (state.words) {
    return;
  }
  view.innerHTML = `<div class="empty"><div class="badge breathe" style="background:var(--accent-soft)">${mochiCat({ mood: 'sleepy' })}</div><p class="muted">Đang tải danh sách từ…</p></div>`;
  const response = await fetch('/api/vocabulary');
  state.words = await response.json();
  for (const word of state.words) {
    word.key = normalizePinyin(word.Pinyin);
    state.byChinese.set(word.Chinese, word);
  }
}

// ---------- sign in ----------

function showSignIn(message = '') {
  document.getElementById('bar').hidden = true;
  let mode = 'login';
  const render = () => {
    view.innerHTML = html`
      <section class="card auth">
        <div class="cats">
          <div class="breathe">${mochiCat({ coat: 'white', mood: 'happy', tail: -1 })}</div>
          <div class="breathe">${mochiCat({ coat: 'grey', mood: 'smile' })}</div>
        </div>
        <h1>Học tiếng Trung</h1>
        <p class="muted">${pendingDesktopLogin()
          ? 'Đăng nhập để kết nối app Học tiếng Trung trên máy tính với tài khoản của bạn.'
          : 'Đăng nhập cùng tài khoản trên web và app để đồng bộ tiến độ học.'}</p>
        <div id="google-area" class="google-area" hidden>
          <div id="google-button"></div>
          <p class="or"><span>hoặc dùng tên đăng nhập</span></p>
        </div>
        <div class="tabs" role="tablist">
          <button type="button" data-mode="login" class="${mode === 'login' ? 'on' : ''}">Đăng nhập</button>
          <button type="button" data-mode="register" class="${mode === 'register' ? 'on' : ''}">Tạo tài khoản</button>
        </div>
        <form id="auth-form" novalidate>
          <label for="user">Tên đăng nhập</label>
          <input id="user" type="text" autocomplete="username" required minlength="3" maxlength="32" value="${escape(state.userName)}">
          <label for="password">Mật khẩu</label>
          <input id="password" type="password" autocomplete="${mode === 'login' ? 'current-password' : 'new-password'}" required minlength="6">
          <p class="error" id="auth-error">${escape(message)}</p>
          <button class="primary" type="submit">${mode === 'login' ? 'Đăng nhập' : 'Tạo tài khoản'}</button>
        </form>
      </section>`;
    view.querySelectorAll('[data-mode]').forEach(button => button.addEventListener('click', () => {
      mode = button.dataset.mode;
      message = '';
      render();
    }));
    view.querySelector('#auth-form').addEventListener('submit', async event => {
      event.preventDefault();
      const userName = view.querySelector('#user').value.trim();
      const password = view.querySelector('#password').value;
      const error = view.querySelector('#auth-error');
      const submit = event.submitter;
      submit.disabled = true;
      try {
        const response = await fetch(`/api/${mode}`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ userName, password })
        });
        const reply = await response.json().catch(() => ({}));
        if (!response.ok) {
          error.textContent = reply.error ?? 'Không đăng nhập được. Hãy thử lại.';
          return;
        }
        signedIn(reply.token, reply.userName);
      } catch {
        error.textContent = 'Không kết nối được máy chủ.';
      } finally {
        submit.disabled = false;
      }
    });
    view.querySelector('#user').focus();
    showGoogleButton(message => { view.querySelector('#auth-error').textContent = message; });
  };
  render();
}

// "Sign in with Google", when this server has a Google client ID: Google's own
// button (its script from accounts.google.com) hands back a signed credential,
// which the server checks with Google before signing the Gmail account in.
let siteConfig = null;
let googleScript = null;

export const GOOGLE_LOGO = `<svg viewBox="0 0 48 48" width="20" height="20" aria-hidden="true">
  <path fill="#EA4335" d="M24 9.5c3.5 0 6.6 1.2 9.1 3.6l6.8-6.8C35.8 2.4 30.3 0 24 0 14.6 0 6.6 5.4 2.7 13.3l7.9 6.1C12.5 13.6 17.8 9.5 24 9.5z"/>
  <path fill="#4285F4" d="M46.1 24.5c0-1.6-.1-3.1-.4-4.5H24v9h12.4c-.5 2.9-2.2 5.3-4.6 7l7.5 5.8c4.4-4 6.8-10 6.8-17.3z"/>
  <path fill="#FBBC05" d="M10.6 28.6c-.5-1.4-.8-3-.8-4.6s.3-3.2.8-4.6l-7.9-6.1C1 16.6 0 20.2 0 24s1 7.4 2.7 10.7l7.9-6.1z"/>
  <path fill="#34A853" d="M24 48c6.5 0 11.9-2.1 15.9-5.8l-7.5-5.8c-2.1 1.4-4.8 2.3-8.4 2.3-6.2 0-11.5-4.1-13.4-9.8l-7.9 6.1C6.6 42.6 14.6 48 24 48z"/>
</svg>`;

// Back from Google's sign-in page: #/google-done?ticket=… is traded for a
// sign-in, then the page the learner started from is shown.
async function finishGoogleSignIn() {
  const [path, query] = location.hash.replace(/^#\/?/, '').split('?');
  if (path !== 'google-done') return false;
  const ticket = new URLSearchParams(query ?? '').get('ticket') ?? '';
  history.replaceState(null, '', '#/');
  try {
    const response = await fetch('/api/login/ticket', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ ticket })
    });
    const reply = await response.json().catch(() => ({}));
    if (!response.ok) {
      showSignIn(reply.error ?? 'Không đăng nhập được bằng Google. Hãy thử lại.');
      return true;
    }
    history.replaceState(null, '', reply.returnTo || '#/');
    signedIn(reply.token, reply.userName);
  } catch {
    showSignIn('Không kết nối được máy chủ.');
  }
  return true;
}

// Why Google's sign-in page sent the learner back without signing in.
function googleProblem() {
  const why = new URLSearchParams(location.hash.split('?')[1] ?? '').get('google');
  return {
    expired: 'Phiên đăng nhập Google đã hết hạn. Hãy bấm đăng nhập lại.',
    cancelled: 'Bạn đã hủy đăng nhập bằng Google.',
    failed: 'Google không xác nhận được tài khoản này. Hãy thử lại.'
  }[why] ?? '';
}

async function showGoogleButton(showError) {
  try {
    siteConfig ??= await fetch('/api/config').then(response => response.json());
  } catch {
    return;
  }
  const clientId = siteConfig?.googleClientId;
  const area = view.querySelector('#google-area');
  if (!clientId || !area) return;
  // Google signs in only on http://localhost or an https:// address with a
  // domain name; opened by IP (http://192.168.…) it refuses (origin_mismatch).
  const byIp = /^[\d.]+$|:/.test(location.hostname);
  if (location.hostname !== 'localhost' && (location.protocol !== 'https:' || byIp)) {
    view.querySelector('#google-button').innerHTML =
      `<p class="google-note">Google chỉ cho đăng nhập khi trang được mở bằng <b>http://localhost:${escape(location.port || '80')}</b>
        trên chính máy chạy app, hoặc qua địa chỉ <b>https://</b> có tên miền, không cho qua địa chỉ IP như
        <b>${escape(location.host)}</b>. Ở đây hãy dùng tên đăng nhập và mật khẩu.</p>`;
    area.hidden = false;
    return;
  }
  if (siteConfig.googleRedirectLogin) {
    // Google's own sign-in page, which also asks to use Gemini for the learner;
    // it comes back to this page (or to the desktop app's request).
    const returnTo = pendingDesktopLogin() ? location.hash : '#/';
    view.querySelector('#google-button').innerHTML =
      `<a class="google-link" href="/api/oauth/google/start?returnTo=${encodeURIComponent(returnTo)}">${GOOGLE_LOGO}<span>Đăng nhập bằng Google</span></a>`;
    area.hidden = false;
    return;
  }
  googleScript ??= new Promise((resolve, reject) => {
    const script = document.createElement('script');
    script.src = 'https://accounts.google.com/gsi/client';
    script.async = true;
    script.onload = resolve;
    script.onerror = reject;
    document.head.append(script);
  });
  try {
    await googleScript;
  } catch {
    googleScript = null;
    showError('Không tải được nút đăng nhập Google. Kiểm tra mạng Internet.');
    return;
  }
  if (!document.body.contains(area)) return;
  google.accounts.id.initialize({
    client_id: clientId,
    ux_mode: 'popup',
    callback: async ({ credential }) => {
      try {
        const response = await fetch('/api/login/google', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ credential })
        });
        const reply = await response.json().catch(() => ({}));
        if (!response.ok) {
          showError(reply.error ?? 'Không đăng nhập được bằng Google. Hãy thử lại.');
          return;
        }
        signedIn(reply.token, reply.userName);
      } catch {
        showError('Không kết nối được máy chủ.');
      }
    }
  });
  google.accounts.id.renderButton(view.querySelector('#google-button'), {
    theme: 'outline', size: 'large', shape: 'pill', text: 'signin_with', locale: 'vi', width: 300
  });
  area.hidden = false;
}

// ---------- signing the desktop app in through the website ----------

// The desktop app opens #/desktop-login?port=…&state=… and waits on that port
// of this PC; once signed in here, the learner allows it and the page posts a
// token of the app's own to it (never in the address bar).
function pendingDesktopLogin() {
  const [path, query] = location.hash.replace(/^#\/?/, '').split('?');
  if (path !== 'desktop-login') return null;
  const params = new URLSearchParams(query ?? '');
  const port = Number(params.get('port'));
  const nonce = params.get('state') ?? '';
  return Number.isInteger(port) && port >= 1024 && port <= 65535 && /^[A-Za-z0-9_-]{16,64}$/.test(nonce)
    ? { port, state: nonce }
    : null;
}

function showDesktopLogin(request) {
  view.innerHTML = html`
    <section class="card auth">
      <div class="cats">
        <div class="breathe">${mochiCat({ coat: 'white', mood: 'happy', tail: -1 })}</div>
        <div class="breathe">${mochiCat({ coat: 'grey', mood: 'surprised' })}</div>
      </div>
      <h1>Kết nối app trên máy tính</h1>
      <p>App <b>Học tiếng Trung</b> trên máy tính này muốn đăng nhập bằng tài khoản <b>${escape(state.userName)}</b> để đồng bộ tiến độ học.</p>
      <p class="error" id="desktop-error"></p>
      <div class="controls">
        <button class="primary" id="allow" type="button">Cho phép</button>
        <a class="button ghost" href="#/">Không, về trang chủ</a>
      </div>
    </section>`;
  view.querySelector('#allow').addEventListener('click', async event => {
    event.target.disabled = true;
    try {
      const response = await fetch('/api/tokens', { method: 'POST', headers: { Authorization: `Bearer ${state.token}` } });
      if (!response.ok) throw new Error(String(response.status));
      const reply = await response.json();
      const form = document.createElement('form');
      form.method = 'POST';
      form.action = `http://127.0.0.1:${request.port}/callback`;
      for (const [name, value] of Object.entries({ token: reply.token, userName: reply.userName, state: request.state })) {
        const field = document.createElement('input');
        field.type = 'hidden';
        field.name = name;
        field.value = value;
        form.append(field);
      }
      document.body.append(form);
      form.submit();
    } catch {
      event.target.disabled = false;
      view.querySelector('#desktop-error').textContent = 'Không kết nối được. Hãy bấm "Đăng nhập bằng Google" trong app một lần nữa.';
    }
  });
}

function signedIn(token, userName) {
  // A different account starts from its own progress, not this browser's.
  if (localStorage.getItem('cl.user')?.toLowerCase() !== userName.toLowerCase()) {
    state.progress = emptyProgress();
    localStorage.removeItem('cl.base');
    saveLocal();
  }
  state.token = token;
  state.userName = userName;
  localStorage.setItem('cl.token', token);
  localStorage.setItem('cl.user', userName);
  startSync();
  route();
}

function signOut(message = '') {
  if (state.token) {
    fetch('/api/logout', { method: 'POST', headers: { Authorization: `Bearer ${state.token}` } }).catch(() => {});
  }
  state.token = null;
  state.sync = null;
  localStorage.removeItem('cl.token');
  showSignIn(message);
}

// ---------- sync ----------

function startSync() {
  const dot = document.getElementById('sync-dot');
  const text = document.getElementById('sync-text');
  state.sync = new SyncClient({
    token: state.token,
    getLocal: () => state.progress,
    setLocal: progress => {
      state.progress = normalize(progress);
      saveLocal();
      refreshCurrent();
    },
    onStatus: ({ state: status, at }) => {
      dot.className = `dot ${status}`;
      text.textContent = status === 'syncing' ? 'Đang đồng bộ…'
        : status === 'synced' ? `Đã đồng bộ ${at.toLocaleTimeString('vi-VN', { hour: '2-digit', minute: '2-digit' })}`
        : 'Chưa đồng bộ';
      // On narrower screens only the dot shows; the status stays in the tooltip.
      dot.parentElement.title = `${text.textContent} · bấm để đồng bộ ngay`;
    },
    onSignedOut: () => signOut('Phiên đăng nhập đã hết hạn. Hãy đăng nhập lại.')
  });
  state.sync.sync();
}

// Pages that show progress re-render when a sync brings in changes; the
// practice page keeps its question and only updates its counters.
let refreshCurrent = () => {};

// ---------- pages ----------

function showHome() {
  const words = state.words;
  const p = state.progress;
  const answered = p.correctAnswers + p.wrongAnswers;
  const typed = new Set(p.typedCorrectWords);
  const missedCount = Object.keys(p.missedWords).length;
  const levels = [...HSK_LEVELS, 'Khác'].map(level => {
    const list = words.filter(word => level === 'Khác' ? !HSK_LEVELS.includes(word.HskLevel) : word.HskLevel === level);
    return { level, total: list.length, done: list.filter(word => typed.has(word.Chinese)).length };
  }).filter(level => level.total > 0);
  const tile = (href, color, coat, mood, title, text) =>
    `<a class="card tile hop" href="${href}">${badge(color, coat, mood)}<strong>${title}</strong><span>${text}</span></a>`;
  view.innerHTML = html`
    <div class="greeting">
      <div><h1>Xin chào, ${escape(state.userName)}!</h1><p class="lead">Hôm nay bạn muốn học gì?</p></div>
      <div class="pair">
        <div class="breathe" style="flex:1">${mochiCat({ coat: 'white', mood: 'happy', tail: -1 })}</div>
        <div class="breathe" style="flex:1">${mochiCat({ coat: 'grey', mood: 'smile' })}</div>
      </div>
    </div>
    <section class="stats">
      <div class="card stat">${badge('#ffe4d6', 'grey', 'smile')}<div><div class="label">Tổng từ vựng</div><div class="value">${words.length.toLocaleString('vi-VN')}</div><div class="note">từ HSK 1 đến HSK 7-9</div></div></div>
      <div class="card stat">${badge('var(--good-soft)', 'white', 'happy')}<div><div class="label">Đang học</div><div class="value">${p.selectedWords.length}</div><div class="note">${(p.selectedWords.length * 100 / words.length).toFixed(1)}% tổng số từ</div></div></div>
      <div class="card stat">${badge('var(--bad-soft)', 'grey', 'sad')}<div><div class="label">Từ gõ sai</div><div class="value">${missedCount}</div><div class="note">${missedCount ? 'Gõ đúng để xóa khỏi danh sách' : 'Không có từ nào sai'}</div></div></div>
      <div class="card stat">${badge('var(--yellow-soft)', 'white', 'surprised')}<div><div class="label">Kết quả luyện gõ</div><div class="value">${answered ? `${p.correctAnswers}/${answered}` : '—'}</div><div class="note">${answered ? `Chính xác ${Math.round(p.correctAnswers * 100 / answered)}%` : 'Chưa luyện gõ lần nào'}</div></div></div>
    </section>
    <section class="tiles">
      ${tile('#/words', '#ffe4d6', 'white', 'smile', 'Danh sách từ', 'Xem, tìm kiếm và chọn từ để học')}
      ${tile('#/flashcard', 'var(--pink-soft)', 'grey', 'happy', 'Flashcard', 'Lật thẻ để nhớ nghĩa, ví dụ và ngữ pháp')}
      ${tile('#/practice', 'var(--good-soft)', 'white', 'happy', 'Luyện gõ', 'Gõ lại từ và câu ví dụ bằng tiếng Trung')}
      ${tile('#/game', 'var(--violet-soft)', 'grey', 'surprised', 'Mèo bắn bong bóng', `Gõ từ để mèo ném len làm vỡ bóng${p.bestGameScore ? ` · cao nhất ${p.bestGameScore}` : ''}`)}
      ${tile('#/translate', 'var(--yellow-soft)', 'white', 'smile', 'Dịch đoạn văn', 'AI viết đoạn văn theo HSK, bạn dịch và được chấm điểm')}
    </section>
    <section class="card levels">
      <h2>Tiến độ luyện gõ theo cấp HSK · số từ đã gõ đúng</h2>
      <div class="level-grid">
        ${levels.map(level => `
          <div class="level"><strong>${level.level}</strong>
            <div class="track"><i style="width:${(level.done * 100 / level.total).toFixed(1)}%"></i></div>
            <span class="muted">${level.done}/${level.total} đúng</span></div>`).join('')}
      </div>
    </section>`;
  refreshCurrent = showHome;
}

const wordList = { query: '', levels: new Set(), page: 0 };

function filteredWords() {
  const query = wordList.query.trim().toLowerCase();
  const pinyin = normalizePinyin(query);
  return state.words.filter(word =>
    (wordList.levels.size === 0 || wordList.levels.has(HSK_LEVELS.includes(word.HskLevel) ? word.HskLevel : 'Khác')) &&
    (!query ||
      word.Chinese.includes(query) ||
      (pinyin && word.key.includes(pinyin)) ||
      (word.Vietnamese ?? '').toLowerCase().includes(query) ||
      (word.English ?? '').toLowerCase().includes(query) ||
      (word.ChineseExample ?? '').includes(query)));
}

function showWords() {
  view.innerHTML = html`
    <h1>Danh sách từ</h1>
    <p class="lead">Flashcard và Luyện gõ chỉ dùng các từ bạn đã tích chọn ở đây.</p>
    <div class="toolbar">
      <input id="search" type="search" placeholder="Tìm chữ Hán, pinyin, nghĩa…" value="${escape(wordList.query)}">
      <button class="ghost" id="select-all" type="button">Chọn tất cả đang hiện</button>
      <button class="ghost" id="clear-all" type="button">Bỏ chọn đang hiện</button>
    </div>
    <div class="chips" id="levels">
      ${[...HSK_LEVELS, 'Khác'].map(level => `<button class="pill ${wordList.levels.has(level) ? 'on' : ''}" type="button" data-level="${level}">${level}</button>`).join('')}
    </div>
    <div id="summary" class="muted"></div>
    <div class="card table-wrap"><table class="word-table">
      <thead><tr><th>Học</th><th>Tiếng Trung</th><th>Pinyin</th><th>Nghĩa tiếng Việt</th><th class="hide-small">Ví dụ</th><th>Cấp</th></tr></thead>
      <tbody id="rows"></tbody>
    </table></div>
    <div class="pager"><button class="pill" id="prev" type="button">← Trước</button><span id="page"></span><button class="pill" id="next" type="button">Sau →</button></div>`;

  const renderRows = () => {
    const list = filteredWords();
    const pages = Math.max(1, Math.ceil(list.length / PAGE_SIZE));
    wordList.page = Math.min(wordList.page, pages - 1);
    const selected = new Set(state.progress.selectedWords);
    const shown = list.slice(wordList.page * PAGE_SIZE, (wordList.page + 1) * PAGE_SIZE);
    view.querySelector('#rows').innerHTML = shown.map(word => html`
      <tr>
        <td><input class="check" type="checkbox" data-word="${escape(word.Chinese)}" ${selected.has(word.Chinese) ? 'checked' : ''} aria-label="Chọn ${escape(word.Chinese)}"></td>
        <td class="hanzi">${escape(word.Chinese)}</td>
        <td class="pinyin">${escape(word.Pinyin)}</td>
        <td>${escape(word.Vietnamese)}</td>
        <td class="hide-small hanzi" style="font-size:16px;color:var(--body)">${escape(word.ChineseExample)}</td>
        <td>${word.HskLevel ? `<span class="hsk">${escape(word.HskLevel)}</span>` : ''}</td>
      </tr>`).join('') || '<tr><td colspan="6" class="center muted">Không có từ nào khớp.</td></tr>';
    view.querySelector('#summary').textContent =
      `${list.length.toLocaleString('vi-VN')} từ · đang học ${state.progress.selectedWords.length} từ`;
    view.querySelector('#page').textContent = `Trang ${wordList.page + 1}/${pages}`;
    view.querySelector('#prev').disabled = wordList.page === 0;
    view.querySelector('#next').disabled = wordList.page >= pages - 1;
  };

  let searchTimer;
  view.querySelector('#search').addEventListener('input', event => {
    clearTimeout(searchTimer);
    searchTimer = setTimeout(() => {
      wordList.query = event.target.value;
      wordList.page = 0;
      renderRows();
    }, 200);
  });
  view.querySelector('#levels').addEventListener('click', event => {
    const level = event.target.dataset?.level;
    if (!level) return;
    wordList.levels.has(level) ? wordList.levels.delete(level) : wordList.levels.add(level);
    event.target.classList.toggle('on');
    wordList.page = 0;
    renderRows();
  });
  view.querySelector('#rows').addEventListener('change', event => {
    const chinese = event.target.dataset?.word;
    if (!chinese) return;
    changeProgress(p => {
      p.selectedWords = event.target.checked
        ? [...p.selectedWords.filter(word => word !== chinese), chinese]
        : p.selectedWords.filter(word => word !== chinese);
    });
    view.querySelector('#summary').textContent =
      `${filteredWords().length.toLocaleString('vi-VN')} từ · đang học ${state.progress.selectedWords.length} từ`;
  });
  const setAll = on => {
    const list = filteredWords().map(word => word.Chinese);
    if (on && list.length > 2000 && !confirm(`Chọn cả ${list.length.toLocaleString('vi-VN')} từ để học?`)) return;
    changeProgress(p => {
      const current = new Set(p.selectedWords);
      p.selectedWords = on
        ? [...p.selectedWords, ...list.filter(word => !current.has(word))]
        : p.selectedWords.filter(word => !list.includes(word));
    });
    renderRows();
  };
  view.querySelector('#select-all').addEventListener('click', () => setAll(true));
  view.querySelector('#clear-all').addEventListener('click', () => setAll(false));
  view.querySelector('#prev').addEventListener('click', () => { wordList.page--; renderRows(); scrollTo(0, 0); });
  view.querySelector('#next').addEventListener('click', () => { wordList.page++; renderRows(); scrollTo(0, 0); });
  renderRows();
  refreshCurrent = renderRows;
}

function noWordsYet(title) {
  view.innerHTML = html`
    <h1>${title}</h1>
    <div class="card empty">
      ${badge('var(--accent-soft)', 'grey', 'sleepy')}
      <p>Chưa chọn từ nào để học.</p>
      <a class="button primary" href="#/words">Chọn từ trong Danh sách từ</a>
    </div>`;
}

function speak(text) {
  if (!('speechSynthesis' in window) || !text) return;
  speechSynthesis.cancel();
  const utterance = new SpeechSynthesisUtterance(text);
  utterance.lang = 'zh-CN';
  utterance.voice = speechSynthesis.getVoices().find(voice => voice.lang.replace('_', '-').startsWith('zh')) ?? null;
  utterance.rate = 0.9;
  speechSynthesis.speak(utterance);
}

let flashIndex = 0;

function showFlashcard() {
  const words = studyWords();
  refreshCurrent = () => {};
  if (words.length === 0) {
    noWordsYet('Flashcard');
    return;
  }
  flashIndex %= words.length;
  const word = words[flashIndex];
  const row = (label, value) => value ? `<div class="row"><b>${label}:</b> ${escape(value)}</div>` : '';
  view.innerHTML = html`
    <h1>Flashcard</h1>
    <p class="lead">Nhấn vào thẻ để lật và xem nghĩa · thẻ ${flashIndex + 1}/${words.length}</p>
    <div class="flash">
      <button class="flip" id="card" type="button" aria-label="Lật thẻ">
        <div class="inner">
          <div class="card face front"><div class="big hanzi">${escape(word.Chinese)}</div></div>
          <div class="card face back">
            <div class="hanzi" style="font-size:40px;color:var(--accent)">${escape(word.Chinese)} <span style="font-size:22px;color:#b4532e">${escape(word.Pinyin)}</span></div>
            ${row('Nghĩa', word.Vietnamese)}${row('Tiếng Anh', word.English)}
            ${row('Ví dụ', word.ChineseExample)}${row('Dịch', word.VietnameseExample)}
            ${row('Ngữ pháp', word.VietnameseGrammar)}
          </div>
        </div>
      </button>
      <div class="controls">
        <button class="primary" id="speak" type="button">🔊 Đọc từ</button>
        <button class="ghost" id="next" type="button">Từ tiếp theo →</button>
      </div>
    </div>`;
  const card = view.querySelector('#card');
  card.addEventListener('click', () => card.classList.toggle('flipped'));
  view.querySelector('#speak').addEventListener('click', () => speak(word.Chinese));
  view.querySelector('#next').addEventListener('click', () => { flashIndex++; showFlashcard(); });
}

const practice = { mode: 'word', queue: [], index: 0, checked: false, timer: null, paused: false, remaining: 0 };

function practiceSeconds() {
  return state.progress.practiceSeconds ?? DEFAULT_PRACTICE_SECONDS;
}

function showPractice() {
  clearInterval(practice.timer);
  const words = studyWords();
  refreshCurrent = () => updateScore();
  if (words.length === 0) {
    noWordsYet('Luyện gõ');
    return;
  }
  if (practice.queue.length === 0 || practice.queue.some(word => !words.includes(word))) {
    practice.queue = shuffle(words);
    practice.index = 0;
  }
  view.innerHTML = html`
    <div class="toolbar" style="justify-content:space-between">
      <h1>Luyện gõ</h1>
      <div class="toolbar" style="margin:0">
        <button class="pill ${practice.mode === 'word' ? 'on' : ''}" data-mode="word" type="button">Gõ từ vựng</button>
        <button class="pill ${practice.mode === 'example' ? 'on' : ''}" data-mode="example" type="button">Gõ câu ví dụ</button>
      </div>
    </div>
    <section class="card practice">
      <p class="muted" id="title"></p>
      <div class="prompt" id="prompt"></div>
      <div class="detail" id="detail"></div>
      <input class="answer ${practice.mode === 'example' ? 'wide' : ''}" id="answer" type="text" lang="zh" autocomplete="off" autocapitalize="off" spellcheck="false" placeholder="Gõ tiếng Trung rồi nhấn Enter">
      <div class="feedback" id="feedback"></div>
      <svg class="clock" viewBox="0 0 84 84" aria-hidden="true"><circle class="back" cx="42" cy="42" r="36"/><circle class="front" id="arc" cx="42" cy="42" r="36" stroke-dasharray="226.2"/><text x="42" y="42" id="seconds"></text></svg>
      <div class="controls">
        <button class="primary" id="restart" type="button">Học lại</button>
        <button class="ghost" id="pause" type="button">Tạm dừng</button>
        <button class="ghost" id="skip" type="button">Bỏ qua</button>
      </div>
      <div class="score">
        <button class="pill" id="missed" type="button"></button>
        <button class="pill" id="correct" type="button"></button>
        <label class="muted" style="display:flex;align-items:center;gap:6px">Mỗi câu
          <input id="duration" type="number" min="1" max="300" value="${practiceSeconds()}" style="width:80px;padding:6px 10px"> giây</label>
      </div>
    </section>
    <dialog id="list"><div class="head"><h2 id="list-title"></h2><button class="ghost" id="close" type="button">Đóng</button></div><div class="body" id="list-body"></div></dialog>`;

  view.querySelectorAll('[data-mode]').forEach(button => button.addEventListener('click', () => {
    practice.mode = button.dataset.mode;
    showPractice();
  }));
  const answer = view.querySelector('#answer');
  answer.addEventListener('keydown', event => {
    // Enter while the input method is still composing picks characters; it is not an answer.
    if (event.key === 'Enter' && !event.isComposing) {
      event.preventDefault();
      if (practice.checked) nextQuestion(); else complete(false);
    }
  });
  view.querySelector('#skip').addEventListener('click', skip);
  view.querySelector('#pause').addEventListener('click', () => {
    practice.paused = !practice.paused;
    view.querySelector('#pause').textContent = practice.paused ? 'Tiếp tục' : 'Tạm dừng';
    answer.disabled = practice.paused || practice.checked;
    if (!practice.paused) answer.focus();
  });
  view.querySelector('#restart').addEventListener('click', () => {
    const p = state.progress;
    const has = p.correctAnswers + p.wrongAnswers > 0 || Object.keys(p.missedWords).length > 0 || p.typedCorrectWords.length > 0;
    if (has && !confirm(`Học lại từ đầu sẽ xóa toàn bộ kết quả luyện gõ (đúng/sai ${p.correctAnswers}/${p.wrongAnswers}, ${Object.keys(p.missedWords).length} từ sai, ${p.typedCorrectWords.length} từ đã đúng). Tiếp tục?`)) return;
    changeProgress(progress => {
      progress.correctAnswers = 0;
      progress.wrongAnswers = 0;
      progress.missedWords = {};
      progress.typedCorrectWords = [];
    });
    practice.queue = [];
    showPractice();
  });
  view.querySelector('#duration').addEventListener('change', event => {
    const seconds = Math.min(300, Math.max(1, Math.round(Number(event.target.value) || DEFAULT_PRACTICE_SECONDS)));
    event.target.value = seconds;
    changeProgress(p => { p.practiceSeconds = seconds; });
  });
  view.querySelector('#missed').addEventListener('click', () => showList('missed'));
  view.querySelector('#correct').addEventListener('click', () => showList('correct'));
  view.querySelector('#close').addEventListener('click', () => view.querySelector('#list').close());
  askQuestion();
}

function currentWord() {
  return practice.queue[practice.index % practice.queue.length];
}

function exampleMode(word) {
  return practice.mode === 'example' && word.ChineseExample;
}

function expectedAnswer(word) {
  return exampleMode(word) ? word.ChineseExample : word.Chinese;
}

function askQuestion() {
  const word = currentWord();
  const example = exampleMode(word);
  view.querySelector('#title').textContent = example
    ? 'Nhìn câu tiếng Việt, gõ lại câu ví dụ tiếng Trung'
    : 'Nhìn nghĩa tiếng Việt và tiếng Anh, gõ từ tiếng Trung';
  view.querySelector('#prompt').textContent = example ? (word.VietnameseExample || word.EnglishExample) : word.Vietnamese;
  view.querySelector('#detail').textContent = example
    ? `Từ cần dùng: ${word.Chinese} (${word.Pinyin}) – ${word.Vietnamese}`
    : (word.English ?? '');
  const feedback = view.querySelector('#feedback');
  feedback.textContent = '';
  feedback.className = 'feedback';
  const answer = view.querySelector('#answer');
  answer.value = '';
  answer.disabled = false;
  answer.focus();
  practice.checked = false;
  practice.paused = false;
  view.querySelector('#pause').textContent = 'Tạm dừng';
  practice.remaining = practiceSeconds();
  updateClock();
  updateScore();
  clearInterval(practice.timer);
  practice.timer = setInterval(() => {
    if (!document.body.contains(answer)) {
      clearInterval(practice.timer);
      return;
    }
    if (practice.paused || practice.checked) return;
    practice.remaining -= 0.25;
    updateClock();
    if (practice.remaining <= 0) complete(true);
  }, 250);
}

function updateClock() {
  const total = practiceSeconds();
  const left = Math.max(0, practice.remaining);
  view.querySelector('#seconds').textContent = Math.ceil(left);
  view.querySelector('#arc').style.strokeDashoffset = String(226.2 * (1 - left / total));
}

function updateScore() {
  const missed = view.querySelector('#missed');
  if (!missed) return;
  missed.textContent = `Xem từ đã sai (${Object.keys(state.progress.missedWords).length})`;
  view.querySelector('#correct').textContent = `Xem từ đã gõ đúng (${state.progress.typedCorrectWords.length})`;
}

function complete(timedOut) {
  if (practice.checked) return;
  const word = currentWord();
  const typed = view.querySelector('#answer').value;
  const correct = !timedOut && (exampleMode(word)
    ? normalizeAnswer(typed) === normalizeAnswer(expectedAnswer(word))
    : typed.trim() === word.Chinese);
  const feedback = view.querySelector('#feedback');
  if (correct) {
    const wasMissed = word.Chinese in state.progress.missedWords;
    changeProgress(p => {
      p.correctAnswers++;
      delete p.missedWords[word.Chinese];
      if (!p.typedCorrectWords.includes(word.Chinese)) p.typedCorrectWords.push(word.Chinese);
    });
    feedback.textContent = wasMissed ? 'Chính xác! Đã xóa từ này khỏi danh sách từ sai.' : 'Chính xác! Giỏi lắm.';
    feedback.className = 'feedback good';
  } else {
    changeProgress(p => {
      p.wrongAnswers++;
      p.missedWords[word.Chinese] = (p.missedWords[word.Chinese] ?? 0) + 1;
      p.typedCorrectWords = p.typedCorrectWords.filter(item => item !== word.Chinese);
    });
    feedback.textContent = `${timedOut ? 'Hết giờ!' : 'Chưa đúng.'} Đáp án: ${expectedAnswer(word)} (${word.Pinyin})`;
    feedback.className = 'feedback bad';
    speak(expectedAnswer(word));
  }
  finishQuestion(correct ? 1200 : 2200);
}

function skip() {
  if (practice.checked || practice.paused) return;
  const word = currentWord();
  changeProgress(p => {
    p.missedWords[word.Chinese] = (p.missedWords[word.Chinese] ?? 0) + 1;
    p.typedCorrectWords = p.typedCorrectWords.filter(item => item !== word.Chinese);
  });
  const feedback = view.querySelector('#feedback');
  feedback.textContent = `Đã bỏ qua. Đáp án: ${expectedAnswer(word)} (${word.Pinyin})`;
  feedback.className = 'feedback bad';
  speak(expectedAnswer(word));
  finishQuestion(1800);
}

function finishQuestion(delay) {
  practice.checked = true;
  view.querySelector('#answer').disabled = true;
  updateScore();
  setTimeout(() => {
    if (practice.checked && document.body.contains(view.querySelector('#answer'))) nextQuestion();
  }, delay);
}

function nextQuestion() {
  practice.index++;
  if (practice.index >= practice.queue.length) {
    practice.queue = shuffle(studyWords());
    practice.index = 0;
  }
  askQuestion();
}

function showList(kind) {
  const p = state.progress;
  const words = kind === 'missed'
    ? Object.entries(p.missedWords).sort((a, b) => b[1] - a[1]).map(([chinese, count]) => ({ word: state.byChinese.get(chinese), count }))
    : p.typedCorrectWords.map(chinese => ({ word: state.byChinese.get(chinese) }));
  view.querySelector('#list-title').textContent = kind === 'missed' ? 'Danh sách từ đã trả lời sai' : 'Danh sách từ đã gõ đúng';
  view.querySelector('#list-body').innerHTML = words.length === 0
    ? '<p class="muted">Chưa có từ nào.</p>'
    : `<table class="word-table"><tbody>${words.filter(item => item.word).map(({ word, count }) => html`
        <tr><td class="hanzi">${escape(word.Chinese)}</td><td class="pinyin">${escape(word.Pinyin)}</td><td>${escape(word.Vietnamese)}</td>
        ${count ? `<td class="muted">sai ${count} lần</td>` : `<td><span class="hsk">${escape(word.HskLevel)}</span></td>`}</tr>`).join('')}</tbody></table>`;
  view.querySelector('#list').showModal();
}

// The game and translation pages live in their own modules; this is what they
// may use of the app.
const pageApp = {
  progress: () => state.progress,
  changeProgress,
  studyWords,
  speak,
  escape,
  // A popped bubble counts as typed right, a landed one as wrong, as on the desktop.
  record(word, hit) {
    speak(word.Chinese);
    changeProgress(p => {
      if (hit) {
        p.correctAnswers++;
        delete p.missedWords[word.Chinese];
        if (!p.typedCorrectWords.includes(word.Chinese)) p.typedCorrectWords.push(word.Chinese);
      } else {
        p.wrongAnswers++;
        p.missedWords[word.Chinese] = (p.missedWords[word.Chinese] ?? 0) + 1;
        p.typedCorrectWords = p.typedCorrectWords.filter(item => item !== word.Chinese);
      }
    });
  }
};

// The module page on screen, stopped when the learner goes elsewhere.
let modulePage = null;

function showModule(show) {
  modulePage = show(view, pageApp);
  refreshCurrent = () => modulePage?.refresh();
}

// ---------- routing ----------

const routes = {
  '': showHome,
  words: showWords,
  flashcard: showFlashcard,
  practice: showPractice,
  game: () => showModule(showGame),
  translate: () => showModule(showTranslate)
};

async function route() {
  if (await finishGoogleSignIn()) return;
  if (!state.token) {
    showSignIn(googleProblem());
    return;
  }
  document.getElementById('bar').hidden = false;
  const desktopLogin = pendingDesktopLogin();
  if (desktopLogin) {
    showDesktopLogin(desktopLogin);
    return;
  }
  await loadWords();
  const page = location.hash.replace(/^#\/?/, '');
  const show = routes[page] ?? showHome;
  // As in the desktop app: "Trang chủ" on Home, "Về trang chủ" on every other page.
  const onHome = show === showHome;
  document.getElementById('home-icon').textContent = onHome ? '🏠' : '←';
  document.getElementById('home-text').textContent = onHome ? 'Trang chủ' : 'Về trang chủ';
  document.getElementById('home-button').classList.toggle('on-home', onHome);
  if (show !== showPractice) clearInterval(practice.timer);
  modulePage?.stop();
  modulePage = null;
  show();
}

addEventListener('hashchange', route);
document.getElementById('sync-button').addEventListener('click', () => state.sync?.sync());
document.getElementById('logout').addEventListener('click', () => signOut());
addEventListener('online', () => state.sync?.sync());
addEventListener('visibilitychange', () => {
  if (document.visibilityState === 'visible') state.sync?.sync();
});
if ('speechSynthesis' in window) speechSynthesis.getVoices();

if (state.token) {
  startSync();
}
route();
