import { AlertTriangle, Inbox, Loader2 } from "lucide-react";
import type { ReactNode } from "react";
import { cn } from "../lib/cn";
import { useUiTexts } from "../lib/texts";
import { Button } from "./button";

export function LoadingState({ label, className }: { label?: string; className?: string }) {
  const texts = useUiTexts();
  return (
    <div role="status" className={cn("flex items-center justify-center gap-2 py-10 text-sm text-muted-foreground", className)}>
      <Loader2 className="size-4 animate-spin" aria-hidden />
      {label ?? texts.loading}
    </div>
  );
}

export function EmptyState({ title, children, action }: { title?: string; children?: ReactNode; action?: ReactNode }) {
  const texts = useUiTexts();
  return (
    <div className="flex flex-col items-center gap-2 py-10 text-center">
      <Inbox className="size-8 text-muted-foreground" aria-hidden />
      <p className="font-medium">{title ?? texts.empty}</p>
      {children ? <div className="max-w-md text-sm text-muted-foreground">{children}</div> : null}
      {action}
    </div>
  );
}

export function ErrorState({ message, onRetry }: { message: string; onRetry?: () => void }) {
  const texts = useUiTexts();
  return (
    <div role="alert" className="flex flex-col items-center gap-2 py-10 text-center">
      <AlertTriangle className="size-8 text-destructive" aria-hidden />
      <p className="font-medium">{texts.errorTitle}</p>
      <p className="max-w-md text-sm text-muted-foreground">{message}</p>
      {onRetry ? (
        <Button variant="outline" size="sm" onClick={onRetry}>
          {texts.retry}
        </Button>
      ) : null}
    </div>
  );
}

/** Skeleton lines while a page loads. */
export function Skeleton({ className }: { className?: string }) {
  return <div className={cn("animate-pulse rounded-md bg-muted", className)} />;
}
