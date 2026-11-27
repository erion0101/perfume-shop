import { cn } from "@perfumeshop/ui";
import { bottleGlass } from "./bottle-glass";

const sizes = {
  card: { cap: "h-9 w-[34px] rounded-[7px_7px_3px_3px]", neck: "h-[9px] w-4", body: "h-[132px] w-[104px] rounded-[18px]", label: "text-[11px]" },
  product: { cap: "h-[52px] w-12 rounded-[9px_9px_4px_4px]", neck: "h-3 w-6", body: "h-[200px] w-[156px] rounded-[26px]", label: "text-sm" },
  hero: { cap: "h-[58px] w-[54px] rounded-[10px_10px_4px_4px]", neck: "h-3.5 w-[26px]", body: "h-[190px] w-[150px] rounded-[26px]", label: "text-[15px]" },
} as const;

/**
 * The drawn bottle from the approved design: gold cap, neck and tinted glass. Used whenever a product has no photo.
 * Decorative by default; pass `title` when it stands in for the product photo.
 */
export function Bottle({ mood, slug, label, size = "card", title, className }: { mood?: string | null; slug: string; label?: string; size?: keyof typeof sizes; title?: string; className?: string }) {
  const s = sizes[size];
  return (
    <div className={cn("bottle flex flex-col items-center", className)} role={title ? "img" : undefined} aria-label={title} aria-hidden={title ? undefined : true}>
      <div className={cn(s.cap, "bg-[linear-gradient(90deg,#7A5E2A,#EBD08F_45%,#7A5E2A)]")} />
      <div className={cn(s.neck, "bg-[#5E4822]")} />
      <div className={cn(s.body, "grid place-items-center border border-[rgba(241,215,154,.4)] px-3 text-center")} style={{ background: bottleGlass(mood, slug) }}>
        {label ? <span className={cn(s.label, "font-display uppercase leading-tight tracking-[0.08em] text-heading")}>{label}</span> : null}
      </div>
    </div>
  );
}
