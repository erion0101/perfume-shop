"use client";

import { cn } from "@perfumeshop/ui";
import { useEffect, useId, useRef, type ReactNode } from "react";
import { t } from "@/i18n";

const focusable = 'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

/**
 * Side panel (cart, mobile menu, filters): modal dialog with focus trap, Escape to close, scroll lock, focus back to
 * the opener. Slides in (instant with prefers-reduced-motion).
 */
export function Drawer({
  open,
  onClose,
  title,
  side = "right",
  children,
  footer,
  closeLabel = t.common.close,
}: {
  open: boolean;
  onClose: () => void;
  title: string;
  side?: "left" | "right";
  children: ReactNode;
  footer?: ReactNode;
  closeLabel?: string;
}) {
  const titleId = useId();
  const panel = useRef<HTMLDivElement>(null);
  const opener = useRef<Element | null>(null);

  useEffect(() => {
    if (!open) {
      return;
    }

    opener.current = document.activeElement;
    const root = document.documentElement;
    const previousOverflow = root.style.overflow;
    root.style.overflow = "hidden";
    const first = panel.current?.querySelector<HTMLElement>(focusable);
    (first ?? panel.current)?.focus();

    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        e.preventDefault();
        onClose();
        return;
      }

      if (e.key !== "Tab" || !panel.current) {
        return;
      }

      const nodes = [...panel.current.querySelectorAll<HTMLElement>(focusable)].filter((n) => n.offsetParent !== null || n === document.activeElement);
      if (nodes.length === 0) {
        e.preventDefault();
        return;
      }

      const firstNode = nodes[0]!;
      const lastNode = nodes[nodes.length - 1]!;
      if (e.shiftKey && document.activeElement === firstNode) {
        e.preventDefault();
        lastNode.focus();
      } else if (!e.shiftKey && document.activeElement === lastNode) {
        e.preventDefault();
        firstNode.focus();
      }
    };

    document.addEventListener("keydown", onKey);
    return () => {
      document.removeEventListener("keydown", onKey);
      root.style.overflow = previousOverflow;
      if (opener.current instanceof HTMLElement) {
        opener.current.focus();
      }
    };
  }, [open, onClose]);

  if (!open) {
    return null;
  }

  return (
    <div className="fixed inset-0 z-50">
      <button type="button" tabIndex={-1} aria-hidden="true" className="absolute inset-0 cursor-default bg-black/60 motion-safe:animate-[fade-in_.2s_ease-out]" onClick={onClose} />
      <div
        ref={panel}
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        tabIndex={-1}
        className={cn(
          "absolute inset-y-0 flex w-full max-w-[420px] flex-col border-line bg-panel shadow-2xl outline-none",
          side === "right" ? "right-0 border-l motion-safe:animate-[slide-in-right_.28s_var(--ease-lift)]" : "left-0 border-r motion-safe:animate-[slide-in-left_.28s_var(--ease-lift)]",
        )}
      >
        <div className="flex items-center justify-between gap-4 border-b border-line px-5 py-4">
          <h2 id={titleId} className="m-0 text-2xl">
            {title}
          </h2>
          <button type="button" onClick={onClose} aria-label={closeLabel} className="grid size-11 place-items-center rounded-full border border-line text-text hover:border-gold">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" aria-hidden="true">
              <path d="M6 6l12 12M18 6 6 18" />
            </svg>
          </button>
        </div>
        <div className="min-h-0 flex-1 overflow-y-auto px-5 py-5">{children}</div>
        {footer ? <div className="border-t border-line px-5 py-5">{footer}</div> : null}
      </div>
    </div>
  );
}
