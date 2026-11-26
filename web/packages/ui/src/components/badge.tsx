import { cva, type VariantProps } from "class-variance-authority";
import type { ComponentProps } from "react";
import { cn } from "../lib/cn";

export const badgeVariants = cva("inline-flex items-center gap-1 rounded-full px-2.5 py-0.5 text-xs font-medium whitespace-nowrap", {
  variants: {
    tone: {
      neutral: "bg-muted text-muted-foreground",
      info: "bg-sky-100 text-sky-800",
      success: "bg-emerald-100 text-emerald-800",
      warning: "bg-amber-100 text-amber-900",
      danger: "bg-red-100 text-red-800",
      brand: "bg-brand-100 text-brand-900",
    },
  },
  defaultVariants: { tone: "neutral" },
});

export type BadgeTone = NonNullable<VariantProps<typeof badgeVariants>["tone"]>;

export function Badge({ className, tone, ...props }: ComponentProps<"span"> & VariantProps<typeof badgeVariants>) {
  return <span className={cn(badgeVariants({ tone }), className)} {...props} />;
}

/**
 * A status from the API (e.g. "Published", "Draft", "LowStock") with its label and colour.
 * The app owns the mapping (texts live in its translation file).
 */
export function StatusBadge<TStatus extends string>({
  status,
  map,
}: {
  status: TStatus;
  map: Partial<Record<TStatus, { label: string; tone: BadgeTone }>>;
}) {
  const entry = map[status];
  return <Badge tone={entry?.tone ?? "neutral"}>{entry?.label ?? status}</Badge>;
}
