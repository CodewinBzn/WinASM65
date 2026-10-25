import type { HostMessage, LiveState, WebviewMessage } from '../shared/protocol.js';

/**
 * What every editor webview shares: the channel to the host, the saved view
 * state, and small DOM builders. Elements are styled by class (the page's
 * stylesheet) or through element.style, never with style attributes, which the
 * content security policy forbids.
 */

interface VsCodeApi {
  postMessage(message: unknown): void;
  getState(): unknown;
  setState(state: unknown): void;
}

declare function acquireVsCodeApi(): VsCodeApi;

const api = acquireVsCodeApi();

export function post(message: WebviewMessage): void {
  api.postMessage(message);
}

/** Calls the handler for every message of the host, then tells the host the page is ready. */
export function start(handler: (message: HostMessage) => void): void {
  window.addEventListener('message', event => handler(event.data as HostMessage));
  post({ type: 'ready' });
}

/** The view state (zoom, tool, toggles) kept by VS Code while the editor is open. */
export function loadState<T extends object>(defaults: T): T {
  const saved = api.getState();
  return { ...defaults, ...(saved && typeof saved === 'object' ? saved : {}) } as T;
}

export function saveState(state: object): void {
  api.setState(state);
}

type Props = {
  className?: string;
  text?: string;
  title?: string;
  [attribute: string]: unknown;
};

/** An element with a class, a text, a title, other properties and children. */
export function el<K extends keyof HTMLElementTagNameMap>(tag: K, props: Props = {}, ...children: Array<Node | string | undefined>): HTMLElementTagNameMap[K] {
  const element = document.createElement(tag);
  for (const [key, value] of Object.entries(props)) {
    if (value === undefined) continue;
    if (key === 'className') element.className = String(value);
    else if (key === 'text') element.textContent = String(value);
    else if (key === 'title') element.title = String(value);
    else if (key.startsWith('on') && typeof value === 'function') element.addEventListener(key.slice(2), value as EventListener);
    else (element as unknown as Record<string, unknown>)[key] = value;
  }
  for (const child of children) {
    if (child !== undefined) element.append(child);
  }
  return element;
}

export function button(label: string, title: string, onClick: () => void, className = ''): HTMLButtonElement {
  return el('button', { type: 'button', className, text: label, title, onclick: onClick });
}

export function separator(): HTMLElement {
  return el('span', { className: 'sep' });
}

/** Buttons of which one is pressed: the tools of an editor. */
export class Choice<T extends string | number> {
  readonly element = el('span', { className: 'group' });
  private readonly buttons = new Map<T, HTMLButtonElement>();

  constructor(options: Array<{ value: T; label: string; title: string }>, private current: T, private readonly onChange: (value: T) => void) {
    for (const option of options) {
      const b = button(option.label, option.title, () => this.set(option.value, true));
      b.setAttribute('aria-pressed', String(option.value === current));
      this.buttons.set(option.value, b);
      this.element.append(b);
    }
  }

  get value(): T {
    return this.current;
  }

  set(value: T, notify = false): void {
    this.current = value;
    for (const [v, b] of this.buttons) {
      b.setAttribute('aria-pressed', String(v === value));
    }
    if (notify) {
      this.onChange(value);
    }
  }
}

export function checkbox(label: string, title: string, checked: boolean, onChange: (checked: boolean) => void): HTMLLabelElement {
  const input = el('input', { type: 'checkbox', checked });
  input.addEventListener('change', () => onChange(input.checked));
  return el('label', { title }, input, label);
}

export function selectBox<T extends string | number>(options: Array<{ value: T; label: string }>, value: T, onChange: (value: T) => void, title = ''): HTMLSelectElement {
  const select = el('select', { title });
  fillSelect(select, options, value);
  select.addEventListener('change', () => {
    const option = options.find(o => String(o.value) === select.value);
    if (option) onChange(option.value);
  });
  return select;
}

export function fillSelect<T extends string | number>(select: HTMLSelectElement, options: Array<{ value: T; label: string }>, value: T): void {
  select.textContent = '';
  for (const option of options) {
    select.append(el('option', { value: String(option.value), text: option.label }));
  }
  select.value = String(value);
}

/** A text input that reports its value when it is committed (Enter or leaving it), not on every key. */
export function textInput(value: string, placeholder: string, onCommit: (value: string) => void): HTMLInputElement {
  const input = el('input', { type: 'text', value, placeholder, spellcheck: false });
  let committed = value;
  const commit = () => {
    if (input.value !== committed) {
      committed = input.value;
      onCommit(input.value);
    }
  };
  input.addEventListener('change', commit);
  input.addEventListener('keydown', e => {
    if (e.key === 'Enter') commit();
    if (e.key === 'Escape') {
      input.value = committed;
      input.blur();
    }
  });
  // set by the editor when the asset changes, without reporting it
  (input as HTMLInputElement & { setValue(v: string): void }).setValue = (v: string) => {
    committed = v;
    if (document.activeElement !== input) input.value = v;
  };
  return input;
}

export function setInputValue(input: HTMLInputElement, value: string): void {
  (input as HTMLInputElement & { setValue?(v: string): void }).setValue?.(value);
}

/** The status line of an editor: a left part that follows the mouse, a notice, and a right part. */
export class StatusBar {
  readonly element = el('div', { className: 'status' });
  private readonly left = el('span');
  private readonly notice = el('span', { className: 'grow' });
  private readonly right = el('span');
  private noticeTimer: number | undefined;

  constructor() {
    this.element.append(this.left, this.notice, this.right);
  }

  hover(text: string): void {
    this.left.textContent = text;
  }

  info(text: string): void {
    this.right.textContent = text;
  }

  show(text: string, level: 'info' | 'error' = 'info'): void {
    this.notice.textContent = text;
    this.notice.className = 'grow' + (level === 'error' ? ' error' : '');
    if (this.noticeTimer) window.clearTimeout(this.noticeTimer);
    if (level === 'info') {
      this.noticeTimer = window.setTimeout(() => { this.notice.textContent = ''; }, 4000);
    }
  }
}

/** A message across the top of an editor (a missing tile set, a file that is not JSON). */
export class Banner {
  readonly element = el('div', { className: 'banner hidden' });

  show(text: string, level: 'warning' | 'error' = 'warning', action?: { label: string; run: () => void }): void {
    this.element.textContent = text;
    this.element.className = 'banner' + (level === 'error' ? ' error' : '');
    if (action) {
      this.element.append(button(action.label, '', action.run));
    }
  }

  hide(): void {
    this.element.className = 'banner hidden';
    this.element.textContent = '';
  }
}

/** Whether a keyboard event belongs to a text field rather than to the editor's shortcuts. */
export function typing(event: KeyboardEvent): boolean {
  const target = event.target as HTMLElement | null;
  return !!target && (target.tagName === 'INPUT' || target.tagName === 'SELECT' || target.tagName === 'TEXTAREA');
}

export function clamp(value: number, min: number, max: number): number {
  return Math.max(min, Math.min(max, value));
}

/**
 * The "Live" button of the toolbar: pressed while edits go into the running
 * machine. The host says whether it can be used (a WinASM65 debug session)
 * and what it does here; clicking asks the host to turn it on or off.
 */
export class LiveToggle {
  readonly element: HTMLButtonElement;
  private state: LiveState = { available: false, on: false, title: 'Live preview: start a WinASM65 debug session first' };

  constructor() {
    this.element = button('● Live', '', () => post({ type: 'live', on: !this.state.on }), 'live-toggle');
    this.update(this.state);
  }

  update(state: LiveState): void {
    this.state = state;
    this.element.disabled = !state.available;
    this.element.title = state.title;
    this.element.setAttribute('aria-pressed', String(state.on && state.available));
    this.element.classList.toggle('on', state.on && state.available);
  }
}
