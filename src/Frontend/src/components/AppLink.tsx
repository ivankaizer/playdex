import type { AnchorHTMLAttributes, MouseEvent } from "react";
import { useAppNavigate } from "@/lib/navigation";

type AppLinkProps = Omit<AnchorHTMLAttributes<HTMLAnchorElement>, "href"> & {
  /** An app path, such as "/sessions" or "/games/abc". */
  to: string;
};

/** A click the browser would turn into a navigation in this tab: not a middle click, not with Ctrl, ⌘, ⇧ or ⌥. */
export function isPlainClick(event: MouseEvent<HTMLAnchorElement>): boolean {
  const target = event.currentTarget.getAttribute("target");
  return (
    event.button === 0 &&
    !event.metaKey &&
    !event.ctrlKey &&
    !event.shiftKey &&
    !event.altKey &&
    (target === null || target === "" || target === "_self")
  );
}

/**
 * A link to a page of the app: a real <a href>, so it opens in a new tab, shows its address on hover and can be
 * copied, while a plain click navigates in place like the rest of the app (no reload, scroll to the top).
 */
export function AppLink({ to, onClick, ...props }: AppLinkProps) {
  const navigate = useAppNavigate();
  return (
    <a
      {...props}
      href={to}
      onClick={(event) => {
        onClick?.(event);
        if (event.defaultPrevented || !isPlainClick(event)) return;
        event.preventDefault();
        navigate(to);
      }}
    />
  );
}
