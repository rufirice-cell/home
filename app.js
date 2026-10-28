(() => {
  'use strict';

  const STORAGE_KEY = 'task-manager.tasks.v1';
  const DAY_MS = 24 * 60 * 60 * 1000;
  const WEEKDAYS = ['日', '月', '火', '水', '木', '金', '土'];

  // 絞り込みの定義。today は「今日」を表す YYYY-MM-DD 文字列。
  const FILTERS = {
    all: () => true,
    active: (task) => !task.done,
    today: (task, today) => !task.done && task.due === today,
    overdue: (task, today) => !task.done && task.due !== '' && task.due < today,
    completed: (task) => task.done,
  };

  const EMPTY_MESSAGES = {
    all: 'タスクはまだありません。上のフォームから追加してください。',
    active: '未完了のタスクはありません。',
    today: '今日が期限のタスクはありません。',
    overdue: '期限切れのタスクはありません。',
    completed: '完了したタスクはありません。',
  };

  const els = {
    form: document.getElementById('task-form'),
    title: document.getElementById('task-title'),
    due: document.getElementById('task-due'),
    list: document.getElementById('task-list'),
    empty: document.getElementById('empty'),
    summary: document.getElementById('summary'),
    search: document.getElementById('search'),
    clearCompleted: document.getElementById('clear-completed'),
    filters: document.querySelectorAll('.filter'),
    template: document.getElementById('task-template'),
  };

  const state = {
    tasks: loadTasks(),
    filter: 'all',
    query: '',
    today: todayKey(),
  };

  // ---- 日付ユーティリティ ----

  function pad(n) {
    return String(n).padStart(2, '0');
  }

  // ローカル時刻での今日を YYYY-MM-DD で返す
  function todayKey() {
    const d = new Date();
    return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
  }

  // YYYY-MM-DD を時差の影響を受けない UTC のミリ秒に変換する
  function keyToUtc(key) {
    const [y, m, d] = key.split('-').map(Number);
    return Date.UTC(y, m - 1, d);
  }

  function daysBetween(fromKey, toKey) {
    return Math.round((keyToUtc(toKey) - keyToUtc(fromKey)) / DAY_MS);
  }

  function formatDate(key) {
    const [y, m, d] = key.split('-').map(Number);
    const weekday = WEEKDAYS[new Date(keyToUtc(key)).getUTCDay()];
    const yearPart = y === Number(state.today.slice(0, 4)) ? '' : `${y}/`;
    return `${yearPart}${m}/${d}（${weekday}）`;
  }

  function dueInfo(task) {
    if (!task.due) return { text: '期限なし', level: 'none' };
    const date = formatDate(task.due);
    if (task.done) return { text: `期限 ${date}`, level: 'done' };
    const diff = daysBetween(state.today, task.due);
    if (diff < 0) return { text: `${date} ・ ${-diff}日超過`, level: 'overdue' };
    if (diff === 0) return { text: `${date} ・ 今日まで`, level: 'today' };
    if (diff === 1) return { text: `${date} ・ 明日まで`, level: 'soon' };
    if (diff <= 3) return { text: `${date} ・ あと${diff}日`, level: 'soon' };
    return { text: `${date} ・ あと${diff}日`, level: 'later' };
  }

  // ---- 保存と読み込み ----

  function loadTasks() {
    try {
      const raw = localStorage.getItem(STORAGE_KEY);
      if (!raw) return [];
      const parsed = JSON.parse(raw);
      if (!Array.isArray(parsed)) return [];
      return parsed
        .filter((t) => t && typeof t.id === 'string' && typeof t.title === 'string')
        .map((t) => ({
          id: t.id,
          title: t.title,
          due: typeof t.due === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(t.due) ? t.due : '',
          done: Boolean(t.done),
          createdAt: Number(t.createdAt) || 0,
        }));
    } catch {
      return [];
    }
  }

  function saveTasks() {
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(state.tasks));
    } catch {
      // プライベートモードなどで保存できない場合は、このタブの中だけで動作させる
    }
  }

  function newId() {
    if (window.crypto && typeof window.crypto.randomUUID === 'function') {
      return window.crypto.randomUUID();
    }
    return `${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 10)}`;
  }

  // ---- 操作 ----

  function addTask(title, due) {
    state.tasks.push({ id: newId(), title, due, done: false, createdAt: Date.now() });
    saveTasks();
    render();
  }

  function toggleTask(id) {
    const task = state.tasks.find((t) => t.id === id);
    if (!task) return;
    task.done = !task.done;
    saveTasks();
    render();
  }

  function deleteTask(id) {
    state.tasks = state.tasks.filter((t) => t.id !== id);
    saveTasks();
    render();
  }

  function clearCompleted() {
    const count = state.tasks.filter((t) => t.done).length;
    if (count === 0) return;
    if (!window.confirm(`完了済みのタスク ${count}件を削除します。よろしいですか？`)) return;
    state.tasks = state.tasks.filter((t) => !t.done);
    saveTasks();
    render();
  }

  // 未完了を先に、その中で期限の近い順（期限なしは最後）、同じなら追加順
  function compareTasks(a, b) {
    if (a.done !== b.done) return a.done ? 1 : -1;
    if (a.due !== b.due) {
      if (!a.due) return 1;
      if (!b.due) return -1;
      return a.due < b.due ? -1 : 1;
    }
    return a.createdAt - b.createdAt;
  }

  // ---- 描画 ----

  function render() {
    const { tasks, filter, today } = state;
    const query = state.query.trim().toLowerCase();

    for (const key of Object.keys(FILTERS)) {
      const n = tasks.filter((t) => FILTERS[key](t, today)).length;
      document.querySelector(`[data-count="${key}"]`).textContent = n;
    }
    els.filters.forEach((btn) => {
      btn.setAttribute('aria-pressed', String(btn.dataset.filter === filter));
    });

    const visible = tasks
      .filter((t) => FILTERS[filter](t, today))
      .filter((t) => !query || t.title.toLowerCase().includes(query))
      .sort(compareTasks);

    const fragment = document.createDocumentFragment();
    for (const task of visible) {
      const item = els.template.content.firstElementChild.cloneNode(true);
      const info = dueInfo(task);
      item.dataset.id = task.id;
      item.classList.toggle('is-done', task.done);
      item.querySelector('.task-toggle').checked = task.done;
      item.querySelector('.visually-hidden').textContent = task.done ? '未完了に戻す' : '完了にする';
      item.querySelector('.task-title').textContent = task.title;
      const due = item.querySelector('.task-due');
      due.textContent = info.text;
      due.dataset.level = info.level;
      item.querySelector('.task-delete').setAttribute('aria-label', `「${task.title}」を削除`);
      fragment.appendChild(item);
    }
    els.list.replaceChildren(fragment);

    els.empty.hidden = visible.length > 0;
    if (visible.length === 0) {
      els.empty.textContent = query
        ? `「${state.query.trim()}」に一致するタスクはありません。`
        : EMPTY_MESSAGES[filter];
    }

    const active = tasks.filter((t) => FILTERS.active(t, today)).length;
    const overdue = tasks.filter((t) => FILTERS.overdue(t, today)).length;
    els.summary.textContent = tasks.length === 0
      ? ''
      : `未完了 ${active}件 / 全 ${tasks.length}件` + (overdue ? ` ・ 期限切れ ${overdue}件` : '');

    els.clearCompleted.disabled = !tasks.some((t) => t.done);
  }

  // ---- イベント ----

  els.form.addEventListener('submit', (event) => {
    event.preventDefault();
    const title = els.title.value.trim();
    if (!title) {
      els.title.value = '';
      els.title.focus();
      return;
    }
    addTask(title, els.due.value);
    els.form.reset();
    els.title.focus();
  });

  els.list.addEventListener('change', (event) => {
    if (!event.target.classList.contains('task-toggle')) return;
    toggleTask(event.target.closest('.task').dataset.id);
  });

  els.list.addEventListener('click', (event) => {
    const button = event.target.closest('.task-delete');
    if (!button) return;
    deleteTask(button.closest('.task').dataset.id);
  });

  els.filters.forEach((btn) => {
    btn.addEventListener('click', () => {
      state.filter = btn.dataset.filter;
      render();
    });
  });

  els.search.addEventListener('input', () => {
    state.query = els.search.value;
    render();
  });

  els.clearCompleted.addEventListener('click', clearCompleted);

  // 別のタブでの変更を反映する
  window.addEventListener('storage', (event) => {
    if (event.key !== STORAGE_KEY) return;
    state.tasks = loadTasks();
    render();
  });

  // 開いたまま日付が変わったら期限表示を更新する
  function refreshToday() {
    const key = todayKey();
    if (key === state.today) return;
    state.today = key;
    render();
  }
  setInterval(refreshToday, 60 * 1000);
  document.addEventListener('visibilitychange', () => {
    if (!document.hidden) refreshToday();
  });

  render();
})();
