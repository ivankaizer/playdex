import { createRoot } from 'react-dom/client';
import Steam from './steam.js';

/**
 * Render a React component to a DOM element
 * @param component The component to render
 * @returns The DOM element that was rendered
 */
export function renderComponent(component: React.ReactNode, tagName = 'div') {
  const container = document.createElement(tagName);
  const root = createRoot(container);
  root.render(component);
  return container;
}

/**
 * Wait for a condition to be true
 * @param condition A method that returns a value that will be checked for truthiness
 * @param interval The initial interval to wait before checking the condition again
 * @returns The value returned by the condition if any
 */
export async function waitFor<T>(
  condition: () => T,
  interval = 10,
): Promise<T> {
  const result = await condition();
  if (result) return result;
  await new Promise((r) => setTimeout(r, interval * 1.1));
  return waitFor(condition, interval);
}

/**
 * Force a fake location change
 */
export function forceFakeLocationChange(
  window: Window = Steam.MainPopup?.window ?? globalThis.window,
) {
  if (Steam.UIStore.MainInstanceUIMode === Steam.UIMode.Desktop)
    Steam.MainWindowBrowserManager.m_lastLocation.hash = `#${Math.random()}`;
  else if (Steam.UIStore.MainInstanceUIMode === Steam.UIMode.Gamepad)
    if (window.opener instanceof Window)
      window.opener.location.hash = `#${Math.random()}`;
}

/**
 * Replacer and reviver for JSON, handling Date objects
 */
export function jsonReplacer(_key: string, value: unknown) {
  if (value instanceof Date) return value.toISOString();
  return value;
}

/**
 * Replacer and reviver for JSON, handling Date objects
 */
export function jsonReviver(_key: string, value: unknown) {
  if (typeof value === 'string') {
    const date = new Date(value);
    if (!Number.isNaN(date.getTime())) return date;
  }
  return value;
}

export type Awaitable<T> = T | Promise<T>;

// biome-ignore lint/suspicious/noConfusingVoidType: on purpose
export type Voidable<T> = T | void;
