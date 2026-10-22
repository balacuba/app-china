// Study progress shared with the desktop app through the server, and the
// three-way merge that combines them. This mirrors ChineseLearning.Sync
// (ProgressMerge.cs) line for line; keep the two in step.

export function emptyProgress() {
  return {
    selectedWords: [],
    typedCorrectWords: [],
    missedWords: {},
    correctAnswers: 0,
    wrongAnswers: 0,
    bestGameScore: 0,
    practiceSeconds: null,
    fallSeconds: null,
    textScale: null,
    pinyinScale: null
  };
}

// Fills in anything missing from a document read from storage or the server.
export function normalize(document) {
  return { ...emptyProgress(), ...(document ?? {}) };
}

export function clone(document) {
  return JSON.parse(JSON.stringify(normalize(document)));
}

// The server's set, plus what this device added, minus what it removed.
export function mergeSet(baseline, local, remote) {
  const before = new Set(baseline);
  const now = new Set(local);
  const removed = new Set([...before].filter(item => !now.has(item)));
  const result = [];
  const seen = new Set();
  for (const item of [...remote, ...local.filter(item => !before.has(item))]) {
    if (!removed.has(item) && !seen.has(item)) {
      seen.add(item);
      result.push(item);
    }
  }
  return result;
}

// Per word: whichever side changed it wins; changed on both, the higher count.
export function mergeCounts(baseline, local, remote) {
  const result = {};
  const keys = new Set([...Object.keys(baseline), ...Object.keys(local), ...Object.keys(remote)]);
  for (const key of keys) {
    const before = key in baseline ? baseline[key] : null;
    const mine = key in local ? local[key] : null;
    const theirs = key in remote ? remote[key] : null;
    const merged = mine === before ? theirs
      : theirs === before ? mine
      : mine === null ? theirs
      : theirs === null ? mine
      : Math.max(mine, theirs);
    if (merged !== null && merged > 0) {
      result[key] = merged;
    }
  }
  return result;
}

function mergeSetting(baseline, local, remote) {
  return local !== baseline ? local : (remote ?? local);
}

export function merge(baseline, local, remote) {
  const b = normalize(baseline);
  const l = normalize(local);
  const r = normalize(remote);
  // A device's first sync: the settings already on the server stand; this
  // device only fills in the ones the server has none of.
  if (!baseline) {
    for (const key of ['practiceSeconds', 'fallSeconds', 'textScale', 'pinyinScale']) {
      l[key] = r[key] ?? l[key];
      b[key] = l[key];
    }
  }
  return {
    selectedWords: mergeSet(b.selectedWords, l.selectedWords, r.selectedWords),
    typedCorrectWords: mergeSet(b.typedCorrectWords, l.typedCorrectWords, r.typedCorrectWords),
    missedWords: mergeCounts(b.missedWords, l.missedWords, r.missedWords),
    correctAnswers: Math.max(0, r.correctAnswers + l.correctAnswers - b.correctAnswers),
    wrongAnswers: Math.max(0, r.wrongAnswers + l.wrongAnswers - b.wrongAnswers),
    bestGameScore: Math.max(l.bestGameScore, r.bestGameScore),
    practiceSeconds: mergeSetting(b.practiceSeconds, l.practiceSeconds, r.practiceSeconds),
    fallSeconds: mergeSetting(b.fallSeconds, l.fallSeconds, r.fallSeconds),
    textScale: mergeSetting(b.textScale, l.textScale, r.textScale),
    pinyinScale: mergeSetting(b.pinyinScale, l.pinyinScale, r.pinyinScale)
  };
}

function same(a, b) {
  return JSON.stringify(normalize(a)) === JSON.stringify(normalize(b));
}

// Keeps this browser's progress (usable offline) in step with the server.
// `getLocal`/`setLocal` read and replace the progress the pages work on.
export class SyncClient {
  constructor({ token, getLocal, setLocal, onStatus, onSignedOut }) {
    this.token = token;
    this.getLocal = getLocal;
    this.setLocal = setLocal;
    this.onStatus = onStatus;
    this.onSignedOut = onSignedOut;
    this.running = null;
    this.again = false;
    this.timer = null;
  }

  get base() {
    const saved = localStorage.getItem('cl.base');
    return saved ? JSON.parse(saved) : null;
  }

  set base(document) {
    localStorage.setItem('cl.base', JSON.stringify(document));
  }

  // Sync a few seconds after the last change, so a burst of answers is one sync.
  schedule(delay = 5000) {
    clearTimeout(this.timer);
    this.timer = setTimeout(() => this.sync(), delay);
  }

  async sync() {
    clearTimeout(this.timer);
    if (this.running) {
      this.again = true;
      return this.running;
    }
    this.running = this.#run().finally(() => {
      this.running = null;
      if (this.again) {
        this.again = false;
        this.schedule(500);
      }
    });
    return this.running;
  }

  async #request(method, body, version) {
    const headers = { Authorization: `Bearer ${this.token}` };
    if (body !== undefined) {
      headers['Content-Type'] = 'application/json';
      headers['If-Match'] = String(version);
    }
    const response = await fetch('/api/progress', { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
    if (response.status === 401) {
      this.onSignedOut();
      throw new Error('signed-out');
    }
    return response;
  }

  async #run() {
    this.onStatus({ state: 'syncing' });
    try {
      // What this device had when the sync began; changes made while it runs
      // are folded in at the end.
      const started = clone(this.getLocal());
      let saved = null;
      for (let attempt = 0; attempt < 5 && !saved; attempt++) {
        const remote = await (await this.#request('GET')).json();
        const merged = merge(this.base, started, remote.progress);
        if (same(merged, remote.progress)) {
          saved = { version: remote.version, progress: normalize(remote.progress) };
          break;
        }
        const response = await this.#request('PUT', merged, remote.version);
        if (response.ok) {
          saved = await response.json();
        } else if (response.status !== 409) {
          throw new Error(`HTTP ${response.status}`);
        }
      }
      if (!saved) {
        throw new Error('conflict');
      }
      this.base = clone(saved.progress);
      this.setLocal(merge(started, this.getLocal(), saved.progress));
      this.onStatus({ state: 'synced', at: new Date() });
    } catch (error) {
      if (error.message !== 'signed-out') {
        this.onStatus({ state: 'offline' });
      }
    }
  }
}
