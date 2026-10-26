// Paragraph translation practice (TranslationPractice.cs + GeminiAi.cs).
// Google Gemini, with the learner's own free API key, writes a short Chinese
// paragraph at the chosen HSK level, the learner types a Vietnamese translation,
// and Gemini marks it out of 10 with comments, corrections and a model
// translation. The key stays in this browser: it is sent only to Google, never
// to our server.
//
// Signed in with Google (and Gemini allowed), no key is needed: the server
// asks Gemini with the learner's own Google access (/api/gemini/generate).

// Always the newest Flash-Lite model on the free tier: it answers in a second or two.
const ENDPOINT = 'https://generativelanguage.googleapis.com/v1beta/models/gemini-flash-lite-latest:generateContent';
const PROXY = '/api/gemini/generate';
const KEY_STORE = 'cl.geminiKey';
const KEY_SHAPE = /^AIza[0-9A-Za-z_-]{35}$/;

const TEACHER_ROLE =
  'Bạn là giáo viên tiếng Trung giỏi, dạy người Việt Nam. Bạn luôn chỉ trả lời bằng một đối tượng JSON hợp lệ, không thêm chữ nào khác.';

// Everyday topics picked at random so new paragraphs do not repeat themselves.
const TOPICS = [
  'gia đình', 'trường học', 'đi chợ', 'ăn uống', 'thời tiết', 'du lịch', 'bạn bè', 'công việc',
  'sở thích', 'mua sắm', 'giao thông', 'sức khỏe', 'ngày nghỉ cuối tuần', 'sinh nhật', 'thể thao',
  'nấu ăn', 'thú cưng', 'học tiếng Trung', 'điện thoại', 'phim ảnh', 'âm nhạc', 'nhà cửa', 'mùa xuân', 'tết'
];

// Paragraph lengths: only a number of sentences. Asked for a character count,
// the AI spends its reply counting characters and never writes the paragraph.
const LENGTHS = [
  { name: 'Ngắn', size: 'dài 2 đến 3 câu ngắn' },
  { name: 'Vừa', size: 'dài 4 đến 5 câu' },
  { name: 'Dài', size: 'dài 6 đến 8 câu' }
];

class AiError extends Error {
  constructor(message, needsKey = false) {
    super(message);
    this.needsKey = needsKey;
  }
}

class RetryLater extends Error {
  constructor(seconds) {
    super('retry');
    this.seconds = seconds;
  }
}

// The key in whatever was pasted: a usual "AIza…" key is picked out of text
// around it (or a key pasted twice); anything else is taken as it is, minus
// spaces and quotes, and Google says whether it works.
function keyFromPaste(text) {
  const usual = new RegExp(KEY_SHAPE.source.replace(/^\^|\$$/g, '') + '(?![0-9A-Za-z_-])').exec(text);
  return usual ? usual[0] : text.replace(/[\s"'`]/g, '');
}

function loadKey() {
  try {
    return localStorage.getItem(KEY_STORE) ?? '';
  } catch {
    return '';
  }
}

const siteToken = () => localStorage.getItem('cl.token') ?? '';

// Whether Gemini can be used through the learner's Google account: null until
// the server has said.
let throughGoogle = null;

async function checkGoogle() {
  try {
    const response = await fetch('/api/gemini/status', { headers: { Authorization: `Bearer ${siteToken()}` } });
    throughGoogle = response.ok && (await response.json()).connected === true;
  } catch {
    throughGoogle = false;
  }
  return throughGoogle;
}

// Asks Gemini whether a key works (listing models costs nothing). Returns null
// when it does, or a message saying why not.
async function checkKey(key) {
  try {
    const response = await fetch('https://generativelanguage.googleapis.com/v1beta/models?pageSize=1', { headers: { 'x-goog-api-key': key } });
    if (response.ok) return null;
    const body = await response.text();
    return body.includes('API_KEY_INVALID')
      ? 'Key không hợp lệ. Hãy sao chép lại key từ Google AI Studio.'
      : `Google từ chối key (${response.status}). Hãy kiểm tra key hoặc thử lại.`;
  } catch {
    return 'Không kết nối được Google để kiểm tra key. Kiểm tra mạng Internet.';
  }
}

// Turns an error reply into something the learner can act on.
function failure(status, body, viaGoogle) {
  let reason = '';
  let said = '';
  try {
    const error = JSON.parse(body).error;
    reason = error?.status ?? '';
    said = typeof error === 'string' ? error : error?.message ?? '';
  } catch { /* not JSON */ }
  if (viaGoogle && (status === 401 || status === 403 || status === 404 || status === 412)) {
    // Signed out, Gemini not allowed (any more), or not switched on for the
    // site's Google project: a key still works meanwhile.
    throughGoogle = false;
    return new AiError(status === 403
      ? `Google chưa cho tài khoản này dùng Gemini qua trang học (${said || reason}). Tạm thời hãy dùng API key (nút 🔑).`
      : `${said || 'Không dùng được Gemini qua tài khoản Google.'} Hoặc dùng API key (nút 🔑).`, true);
  }
  if (body.includes('API_KEY_INVALID') || status === 401 || status === 403) {
    return new AiError('API key Gemini không hợp lệ hoặc không có quyền. Hãy kiểm tra lại key (nút 🔑).', true);
  }
  if (status === 429 || reason === 'RESOURCE_EXHAUSTED') {
    // A short wait (e.g. "retryDelay": "21s") is waited out; the daily limit is not.
    const delay = /"retryDelay"\s*:\s*"(\d+)(?:\.\d+)?s"/.exec(body);
    return delay && Number(delay[1]) <= 60
      ? new RetryLater(Number(delay[1]) + 1)
      : new AiError('Đã hết lượt dùng Gemini miễn phí lúc này (giới hạn theo phút hoặc theo ngày). Hãy thử lại sau.');
  }
  if (status >= 500) return new RetryLater(3);
  return new AiError(`Gemini báo lỗi (${status} ${reason}). Hãy thử lại.`);
}

// `key` is the learner's API key, or null to go through their Google account.
async function askOnce(key, system, question, signal) {
  // Gives up after 90 seconds, or when the learner starts something else.
  const timeout = AbortSignal.timeout(90000);
  const response = await fetch(key ? ENDPOINT : PROXY, {
    method: 'POST',
    headers: key
      ? { 'Content-Type': 'application/json', 'x-goog-api-key': key }
      : { 'Content-Type': 'application/json', Authorization: `Bearer ${siteToken()}` },
    body: JSON.stringify({
      systemInstruction: { parts: [{ text: system }] },
      contents: [{ role: 'user', parts: [{ text: question }] }],
      // A little below the default: still varied, but steadier and more coherent.
      generationConfig: { responseMimeType: 'application/json', temperature: 0.7 }
    }),
    signal: AbortSignal.any ? AbortSignal.any([signal, timeout]) : signal
  });
  const body = await response.text();
  if (!response.ok) throw failure(response.status, body, !key);
  // The answer is the text of the first candidate's parts, leaving out any
  // "thought" parts.
  const parts = JSON.parse(body).candidates?.[0]?.content?.parts ?? [];
  const reply = parts.filter(part => typeof part.text === 'string' && part.thought !== true).map(part => part.text).join('');
  const start = reply.indexOf('{');
  const end = reply.lastIndexOf('}');
  if (start < 0 || end <= start) throw new AiError('Gemini chưa trả lời xong. Hãy thử lại.');
  return JSON.parse(reply.slice(start, end + 1));
}

// Sends one question and returns the JSON object in the answer. `progress`
// receives `working` while Gemini answers, and a countdown if the free tier asks
// to wait. Busy or briefly rate-limited replies are retried.
async function askJson(system, question, working, progress, signal) {
  // The Google account first (nothing to set up); otherwise the key.
  const key = (throughGoogle ?? await checkGoogle()) ? null : loadKey();
  if (key === '') throw new AiError('Hãy nhập API key Gemini trước (nút 🔑), hoặc đăng nhập bằng Google.', true);
  let last = null;
  for (let attempt = 0; attempt < 3; attempt++) {
    progress(working);
    try {
      return await askOnce(key, system, question, signal);
    } catch (error) {
      if (signal.aborted) throw error;
      last = error;
      if (error instanceof RetryLater) {
        for (let seconds = error.seconds; seconds > 0; seconds--) {
          progress(`⏳ Gemini miễn phí đang giới hạn lượt hỏi, chờ ${seconds} giây…`);
          await new Promise(resolve => setTimeout(resolve, 1000));
          if (signal.aborted) throw error;
        }
      } else if (error instanceof AiError && (error.needsKey || attempt === 2)) {
        throw error;
      }
    }
  }
  if (last instanceof RetryLater) throw new AiError('Gemini miễn phí đang quá tải. Hãy thử lại sau ít phút.');
  if (last?.name === 'TimeoutError') throw new AiError('Gemini trả lời quá lâu. Hãy thử lại.');
  if (last instanceof TypeError) throw new AiError('Không kết nối được Gemini. Kiểm tra mạng Internet rồi thử lại.');
  if (last instanceof AiError) throw last;
  throw new AiError('Gemini trả lời không đúng dạng. Hãy thử lại.');
}

const text = value => (value === null || value === undefined ? '' : String(value)).trim();

// Kept between visits to the page, so a paragraph is not lost by looking at
// another page.
const page = {
  level: 2,
  length: 0,
  topic: '',
  chinese: '',
  pinyin: '',
  reference: '',
  showPinyin: false,
  translation: '',
  result: '',
  status: '',
  busy: null
};

// Shows the page in `view`. `app` gives the words being studied, `escape` and
// `speak`. Returns what the router calls when the learner leaves.
export function showTranslate(view, app) {
  const { escape } = app;
  view.innerHTML = `
    <div class="toolbar" style="justify-content:space-between">
      <h1>Dịch đoạn văn</h1>
      <button class="ghost" id="key" type="button" title="API key Gemini">🔑 API key</button>
    </div>
    <p class="lead">Gemini viết một đoạn văn tiếng Trung theo trình độ của bạn; bạn dịch sang tiếng Việt rồi Gemini chấm điểm.</p>
    <section class="card translate">
      <div class="choice-row"><span class="caption">Trình độ</span>
        <div class="chips" id="levels">${Array.from({ length: 9 }, (_, index) => `<button class="pill" type="button" data-level="${index + 1}">HSK ${index + 1}</button>`).join('')}</div></div>
      <div class="choice-row"><span class="caption">Độ dài</span>
        <div class="chips" id="lengths">${LENGTHS.map((length, index) => `<button class="pill" type="button" data-length="${index}">${length.name}</button>`).join('')}</div>
        <button class="primary" id="new" type="button">✨ Đoạn văn mới</button></div>
      <div class="paragraph">
        <div class="paragraph-top">
          <span class="topic" id="topic"></span>
          <span class="controls">
            <button class="ghost small" id="speak" type="button">🔊 Nghe</button>
            <button class="ghost small" id="pinyin-toggle" type="button"></button>
          </span>
        </div>
        <div class="chinese hanzi" id="chinese" lang="zh"></div>
        <div class="pinyin-line" id="pinyin"></div>
      </div>
      <label class="caption" for="translation">Bản dịch của bạn (tiếng Việt)</label>
      <textarea id="translation" rows="4" placeholder="Gõ bản dịch tiếng Việt của bạn ở đây…"></textarea>
      <div class="actions">
        <button class="primary" id="grade" type="button">✅ Chấm bài</button>
        <button class="ghost" id="answer" type="button">👁 Xem bản dịch mẫu</button>
        <span class="muted" id="status" role="status"></span>
      </div>
      <div class="result" id="result"></div>
    </section>
    <dialog id="key-dialog">
      <div class="head"><h2>API key Gemini</h2><button class="ghost" id="key-close" type="button">Đóng</button></div>
      <form class="body key-form" id="key-form" novalidate>
        <p>Trang này dùng Google Gemini miễn phí bằng API key của chính bạn. Lấy key tại
          <a href="https://aistudio.google.com/app/apikey" target="_blank" rel="noopener">Google AI Studio</a> (đăng nhập Google → “Create API key”), rồi dán vào đây.</p>
        <p class="muted">Key chỉ được lưu trong trình duyệt này và chỉ gửi tới Google, không gửi lên máy chủ của trang học.
          Đăng nhập trang học bằng Google (và đồng ý quyền Gemini) thì không cần key.</p>
        <input id="key-input" type="password" autocomplete="off" spellcheck="false" placeholder="AIza…">
        <p class="error" id="key-error"></p>
        <div class="controls" style="justify-content:flex-start">
          <button class="primary" type="submit">Kiểm tra và lưu</button>
          <button class="ghost" id="key-delete" type="button">Xóa key</button>
        </div>
      </form>
    </dialog>`;

  const $ = selector => view.querySelector(selector);
  const translation = $('#translation');
  const keyDialog = $('#key-dialog');

  // A request can finish after the learner has gone to another page: its answer
  // is kept in `page` and shown on the next visit.
  const onScreen = () => document.body.contains(translation);

  const setStatus = message => {
    page.status = message;
    if (onScreen()) $('#status').textContent = message;
  };

  function render() {
    if (!onScreen()) return;
    view.querySelectorAll('[data-level]').forEach(button => button.classList.toggle('on', Number(button.dataset.level) === page.level));
    view.querySelectorAll('[data-length]').forEach(button => button.classList.toggle('on', Number(button.dataset.length) === page.length));
    const has = page.reference.length > 0;
    const busy = page.busy !== null;
    $('#topic').textContent = has ? `📝 Chủ đề: ${page.topic} • HSK ${page.paragraphLevel}` : '';
    $('#chinese').textContent = page.chinese || 'Bấm “Đoạn văn mới” để AI viết một đoạn văn ngắn cho bạn dịch.';
    $('#chinese').classList.toggle('placeholder', !page.chinese);
    $('#pinyin').textContent = page.pinyin;
    $('#pinyin').hidden = !page.showPinyin || !page.pinyin;
    $('#pinyin-toggle').textContent = page.showPinyin ? 'Ẩn pinyin' : 'Hiện pinyin';
    $('#speak').disabled = !has;
    $('#pinyin-toggle').disabled = !has;
    $('#new').disabled = busy;
    $('#grade').disabled = busy || !has;
    $('#answer').disabled = busy || !has;
    translation.rows = page.length === 2 ? 7 : 4;
    $('#result').innerHTML = page.result;
    $('#status').textContent = page.status;
  }

  function openKey() {
    $('#key-input').value = loadKey();
    $('#key-error').textContent = '';
    keyDialog.showModal();
  }

  function fail(error) {
    page.busy = null;
    if (error?.name === 'AbortError' && !(error instanceof AiError)) return;
    setStatus('⚠ ' + (error instanceof AiError ? error.message : 'AI trả lời không đúng dạng. Hãy thử lại.'));
    render();
    if (error instanceof AiError && error.needsKey && onScreen()) openKey();
  }

  function begin(message) {
    page.busy?.abort();
    const controller = new AbortController();
    page.busy = controller;
    setStatus(message);
    render();
    return controller;
  }

  // Only updates the page if it is still the one on screen and still the
  // latest request.
  const current = controller => page.busy === controller;

  async function newParagraph() {
    const controller = begin('⏳ Gemini đang viết đoạn văn…');
    // A couple of the learner's words are only offered, never required: forcing
    // them in made paragraphs stray off topic and stop making sense.
    const words = app.studyWords().map(word => word.Chinese).filter(Boolean)
      .sort(() => Math.random() - 0.5).slice(0, 2);
    const topic = TOPICS[Math.floor(Math.random() * TOPICS.length)];
    const level = page.level;
    const question =
      `Viết một đoạn văn tiếng Trung giản thể ${LENGTHS[page.length].size}, ` +
      `chỉ dùng từ vựng và ngữ pháp ở trình độ HSK ${level}${level >= 7 ? ' (bậc cao cấp 7–9 của chuẩn HSK 3.0)' : ''}, về chủ đề "${topic}". Không cần đếm chữ. ` +
      'Đoạn văn kể một tình huống cụ thể, liền mạch, có nhân vật và sự việc rõ ràng (ví dụ: ai, ở đâu, làm gì, vì sao), ' +
      'các câu nối tiếp nhau hợp lý như một bài đọc trong giáo trình. Mọi câu đều phải nói về chủ đề; ' +
      'không chào hỏi, không cảm ơn, không nói chuyện với người đọc, không chèn câu lạc đề. ' +
      (words.length > 0 ? `Chỉ khi thật sự hợp với tình huống thì mới dùng từ sau, nếu không hợp thì bỏ qua: ${words.join('、')}. ` : '') +
      'Trả về JSON: {"topic": "tên chủ đề bằng tiếng Việt", "chinese": "đoạn văn chữ Hán", ' +
      '"pinyin": "pinyin có dấu thanh của cả đoạn", "vietnamese": "bản dịch tiếng Việt tự nhiên, sát nghĩa"}';
    try {
      const answer = await askJson(TEACHER_ROLE, question, '⏳ Gemini đang viết đoạn văn…',
        message => { if (current(controller)) setStatus(message); }, controller.signal);
      if (!current(controller)) return;
      const chinese = text(answer.chinese);
      if (!chinese) throw new AiError('AI chưa viết được đoạn văn. Hãy thử lại.');
      Object.assign(page, {
        busy: null,
        topic: text(answer.topic),
        chinese,
        pinyin: text(answer.pinyin),
        reference: text(answer.vietnamese),
        paragraphLevel: level,
        translation: '',
        result: '',
        status: ''
      });
      translation.value = '';
      render();
      if (onScreen()) translation.focus();
    } catch (error) {
      if (current(controller)) fail(error);
    }
  }

  async function grade() {
    const typed = translation.value.trim();
    if (!typed) {
      setStatus('Hãy gõ bản dịch trước khi chấm.');
      translation.focus();
      return;
    }
    const controller = begin('⏳ Gemini đang chấm bài…');
    const question =
      `Đoạn văn tiếng Trung: ${page.chinese}\n` +
      `Bản dịch tham khảo: ${page.reference}\n` +
      `Bản dịch tiếng Việt của học viên: ${typed}\n\n` +
      'Hãy chấm bản dịch của học viên theo thang điểm 0 đến 10, dựa trên việc dịch đúng nghĩa, đủ ý và tự nhiên ' +
      '(không trừ điểm vì cách diễn đạt khác nhưng đúng nghĩa). Viết nhận xét ngắn gọn, thân thiện bằng tiếng Việt. ' +
      'Trả về JSON: {"score": số từ 0 đến 10, "feedback": "nhận xét 1 đến 3 câu", ' +
      '"mistakes": [{"chinese": "cụm chữ Hán bị dịch sai hoặc bỏ sót", "yours": "phần học viên đã dịch (để trống nếu bỏ sót)", "better": "cách dịch đúng"}], ' +
      '"better_translation": "bản dịch hoàn chỉnh, sửa lại từ bản của học viên"}';
    try {
      const answer = await askJson(TEACHER_ROLE, question, '⏳ Gemini đang chấm bài…',
        message => { if (current(controller)) setStatus(message); }, controller.signal);
      if (!current(controller)) return;
      page.busy = null;
      page.result = gradeHtml(answer);
      page.status = '';
      render();
      if (onScreen()) $('#result').scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    } catch (error) {
      if (current(controller)) fail(error);
    }
  }

  const referenceHtml = () => `<h3>Bản dịch mẫu</h3><p>${escape(page.reference)}</p>`;

  // Score with a word of encouragement, the comment, each correction, and the
  // corrected and model translations.
  function gradeHtml(answer) {
    const value = Number.parseFloat(text(answer.score));
    const score = Number.isFinite(value) ? Math.min(10, Math.max(0, value)) : -1;
    const [cheer, tone] = score >= 9 ? ['Xuất sắc! 🎉', 'good']
      : score >= 7 ? ['Tốt lắm! 👍', 'good']
      : score >= 5 ? ['Khá rồi, cố thêm chút nữa 💪', 'warn']
      : score >= 0 ? ['Cần luyện thêm 📚', 'bad']
      : ['Đã chấm xong', 'accent'];
    const mistakes = Array.isArray(answer.mistakes) ? answer.mistakes : [];
    const better = text(answer.better_translation);
    return `
      <div class="grade ${tone}">${score >= 0 ? `<span class="points">${Math.round(score * 10) / 10}/10</span>` : ''}<span class="cheer">${cheer}</span></div>
      <p>${escape(text(answer.feedback))}</p>
      ${mistakes.length > 0 ? `<h3>Cần sửa</h3><ul class="mistakes">${mistakes.map(mistake => {
        const yours = text(mistake?.yours);
        return `<li><b class="hanzi">${escape(text(mistake?.chinese))}</b>
          <span class="yours">${yours ? `bạn dịch: “${escape(yours)}”` : '(bỏ sót)'}</span>
          <span class="better">→ ${escape(text(mistake?.better))}</span></li>`;
      }).join('')}</ul>` : ''}
      ${better ? `<h3>Bản dịch của bạn sau khi sửa</h3><p>${escape(better)}</p>` : ''}
      ${referenceHtml()}`;
  }

  view.querySelector('#levels').addEventListener('click', event => {
    if (!event.target.dataset?.level) return;
    page.level = Number(event.target.dataset.level);
    render();
  });
  view.querySelector('#lengths').addEventListener('click', event => {
    if (!event.target.dataset?.length) return;
    page.length = Number(event.target.dataset.length);
    render();
  });
  $('#new').addEventListener('click', newParagraph);
  $('#grade').addEventListener('click', grade);
  $('#answer').addEventListener('click', () => {
    page.result = referenceHtml();
    render();
  });
  $('#speak').addEventListener('click', () => app.speak(page.chinese));
  $('#pinyin-toggle').addEventListener('click', () => {
    page.showPinyin = !page.showPinyin;
    render();
  });
  translation.value = page.translation;
  translation.addEventListener('input', () => { page.translation = translation.value; });

  $('#key').addEventListener('click', openKey);
  $('#key-close').addEventListener('click', () => keyDialog.close());
  $('#key-delete').addEventListener('click', () => {
    localStorage.removeItem(KEY_STORE);
    keyDialog.close();
    setStatus('Đã xóa API key.');
  });
  $('#key-form').addEventListener('submit', async event => {
    event.preventDefault();
    const key = keyFromPaste($('#key-input').value);
    const error = $('#key-error');
    if (key.length < 20) {
      error.textContent = 'Chưa có key. Hãy dán API key Gemini sao chép từ Google AI Studio.';
      return;
    }
    $('#key-input').value = key;
    const submit = event.submitter;
    submit.disabled = true;
    error.textContent = 'Đang kiểm tra key với Google…';
    const problem = await checkKey(key);
    submit.disabled = false;
    if (problem) {
      error.textContent = problem;
      return;
    }
    localStorage.setItem(KEY_STORE, key);
    keyDialog.close();
    setStatus('✅ Đã lưu API key. Bấm “Đoạn văn mới” để bắt đầu.');
  });

  const NEEDS_KEY = 'Cần API key Gemini miễn phí: bấm nút 🔑 để nhập, hoặc đăng nhập bằng Google.';
  checkGoogle().then(connected => {
    if (page.busy !== null || (page.status && page.status !== NEEDS_KEY)) return;
    setStatus(connected ? '✅ Dùng Gemini qua tài khoản Google của bạn, không cần API key.' : loadKey() ? '' : NEEDS_KEY);
  });
  render();

  return {
    refresh() {},
    // Leaving the page lets a running request finish; its answer is kept for
    // when the learner comes back.
    stop() {}
  };
}
