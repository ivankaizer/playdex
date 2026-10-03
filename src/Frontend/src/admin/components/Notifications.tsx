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
  const value = useMemo<Notifications>(
    () => ({ success: (s, d) => push("success", s, d), error: (s, d) => push("error", s, d) }),
    [push],
  );

  return (
    <NotificationContext.Provider value={value}>
      {children}
      <div aria-live="polite" className="pointer-events-none fixed bottom-4 right-4 z-50 flex w-80 max-w-[calc(100vw-2rem)] flex-col gap-2">
        {notices.map((notice) => (
          <div
            key={notice.id}
            role={notice.tone === "error" ? "alert" : "status"}
            className={cx(
              "pointer-events-auto rounded-md border px-4 py-3 text-sm shadow-lg",
              notice.tone === "success"
                ? "border-emerald-200 bg-emerald-50 text-emerald-900 dark:border-emerald-800 dark:bg-emerald-950 dark:text-emerald-100"
                : "border-rose-200 bg-rose-50 text-rose-900 dark:border-rose-800 dark:bg-rose-950 dark:text-rose-100",
            )}
          >
            <div className="flex items-start justify-between gap-2">
              <strong className="font-semibold">{notice.summary}</strong>
              <button type="button" aria-label="Dismiss" className="opacity-60 hover:opacity-100" onClick={() => dismiss(notice.id)}>
                ×
              </button>
            </div>
            <p className="mt-0.5">{notice.detail}</p>
          </div>
        ))}
      </div>
    </NotificationContext.Provider>
  );
}
