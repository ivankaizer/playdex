import { type RefObject, useEffect } from "react";
import { inHost, onHostMessage, postToHost } from "../host/bridge";
import { useStore } from "../nav/store";
import { palette, scheme } from "../theme";

declare global {
  interface Window {
    /** Desktop mode in the Windows app: the room the window's caption buttons take over the top bar, in CSS pixels. */
    __PLAYDEX_CAPTION__?: { right: number; height: number };
  }
}

/** The bar leaves `right` free for the caption buttons and is as tall as they are (desktop.css, [data-caption]). */
function applyCaption(caption: { right: number; height: number }) {
  const root = document.documentElement;
  root.style.setProperty("--caption-right", `${caption.right}px`);
  root.style.setProperty("--caption-h", `${caption.height}px`);
  root.dataset.caption = "";
}

if (window.__PLAYDEX_CAPTION__) applyCaption(window.__PLAYDEX_CAPTION__);

/**
 * In the Windows app, desktop mode's top bar is the window's title bar (the host's ShellTitleBar.cs): Windows draws the
 * caption buttons over its right end. This tells the host where the bar is empty (the elements marked
 * data-titlebar-drag), which is what drags the window, and the theme's ink, which the caption buttons are drawn in;
 * again whenever the layout, the light or dark scheme or the palette changes. In a plain browser it does nothing.
 */
export function useTitleBar(bar: RefObject<HTMLElement | null>) {
  const dark = useStore(scheme);
  const colors = useStore(palette);

  useEffect(() => {
    if (!inHost) return;
    return onHostMessage((m) => {
      if (m.type === "caption") applyCaption(m);
    });
  }, []);

  useEffect(() => {
    const el = bar.current;
    if (!inHost || !el) return;
    let timer = 0;
    const send = () => {
      window.clearTimeout(timer);
      // After the layout settles, and after a palette's stylesheet has applied its ink.
      timer = window.setTimeout(() => {
        const ink = getComputedStyle(document.documentElement)
          .getPropertyValue("--ink")
          .trim()
          .split(/[\s,]+/)
          .map(Number);
        const drag = [...el.querySelectorAll<HTMLElement>("[data-titlebar-drag]")]
          .map((d) => d.getBoundingClientRect())
          .filter((r) => r.width > 0 && r.height > 0)
          .map((r) => ({ x: r.left, y: r.top, width: r.width, height: r.height }));
        postToHost({ type: "title-bar", ink: ink.length === 3 && ink.every(Number.isFinite) ? ink : undefined, drag });
      }, 50);
    };
    send();
    const resized = new ResizeObserver(send);
    resized.observe(el);
    for (const d of el.querySelectorAll("[data-titlebar-drag]")) resized.observe(d);
    // A palette's stylesheet loads after the store changes.
    const sheets = new MutationObserver(send);
    sheets.observe(document.head, { childList: true, subtree: true, attributes: true });
    return () => {
      window.clearTimeout(timer);
      resized.disconnect();
      sheets.disconnect();
    };
  }, [bar, dark, colors]);
}
