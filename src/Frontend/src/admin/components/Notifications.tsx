import { createContext, useCallback, useContext, useMemo, useRef, useState, type ReactNode } from "react";
import { cx } from "@/lib/cx";

interface Notice {
  id: number;
  tone: "success" | "error";
  summary: string;
  detail: string;
}

interface Notifications {
  success: (summary: string, detail: string) => void;
  error: (summary: string, detail: string) => void;
}

const NotificationContext = createContext<Notifications>({ success: () => {}, error: () => {} });

export function useNotifications(): Notifications {
  return useContext(NotificationContext);
}

export function NotificationProvider({ children }: { children: ReactNode }) {
  const [notices, setNotices] = useState<Notice[]>([]);
  const nextId = useRef(0);

  const dismiss = useCallback((id: number) => setNotices((current) => current.filter((notice) => notice.id !== id)), []);
  const push = useCallback(
    (tone: Notice["tone"], summary: string, detail: string) => {
      const id = ++nextId.current;
      setNotices((current) => [...current, { id, tone, summary, detail }]);
      window.setTimeout(() => dismiss(id), 5000);
    },
    [dismiss],
  );
  const value = useMemo<Notifications>(() => ({ success: (s, d) => push("success", s, d), error: (s, d) => push("error", s, d) }), [push]);

  return (
    <NotificationContext.Provider value={value}>
      {children}
      <div
        aria-live="polite"
        className="pointer-events-none fixed bottom-8 left-1/2 z-[80] flex w-[min(420px,calc(100vw-32px))] -translate-x-1/2 flex-col items-center gap-2"
      >
        {notices.map((notice) => (
          <div
            key={notice.id}
            role={notice.tone === "error" ? "alert" : "status"}
            className="pdx-toast pointer-events-auto !static flex w-full !transform-none items-start gap-3 !rounded-[22px] !px-5 !py-3"
          >
            <span
              aria-hidden="true"
              className={cx("mt-[7px] h-2 w-2 shrink-0 rounded-full", notice.tone === "success" ? "bg-[#6fe3b5]" : "bg-[#f0788a]")}
            />
            <div className="min-w-0 flex-1">
              <strong className="block font-semibold">{notice.summary}</strong>
              <p className="mt-0.5 break-words text-[13px] text-[#c3ccd5]">{notice.detail}</p>
            </div>
            <button
              type="button"
              aria-label="Dismiss"
              className="pdx-hit -mr-1 grid h-7 w-7 shrink-0 place-items-center rounded-full text-[#c3ccd5] transition hover:bg-white/10 hover:text-white"
              onClick={() => dismiss(notice.id)}
            >
              ×
            </button>
          </div>
        ))}
      </div>
    </NotificationContext.Provider>
  );
}
