import { cn } from "@perfumeshop/ui";
import { capitalize, noteName, t } from "@/i18n";

type Notes = { top: { name: string }[]; heart: { name: string }[]; base: { name: string }[] };

const list = (notes: { name: string }[]) => capitalize(notes.map((n) => noteName(n.name)).join(", "));

/**
 * The arch-shaped pyramid of the approved design: arch on top (Në fillim), Zemra, and the gold base (what stays).
 * Layers without notes are left out.
 */
export function NotePyramid({ notes, className }: { notes: Notes; className?: string }) {
  const layers = [
    { key: "top", label: t.product.top, notes: notes.top, shape: "w-[64%] sm:w-[58%] rounded-[200px_200px_10px_10px] border border-line-strong pt-8" },
    { key: "heart", label: t.product.heart, notes: notes.heart, shape: "w-[82%] sm:w-[78%] rounded-[10px] border border-line-strong" },
    { key: "base", label: t.product.base, notes: notes.base, shape: "w-full sm:w-[98%] rounded-[10px] bg-gold text-ground" },
  ].filter((l) => l.notes.length > 0);

  if (layers.length === 0) {
    return null;
  }

  return (
    <figure className={cn("m-0", className)}>
      <figcaption className="sr-only">{t.product.pyramid}</figcaption>
      <ol className="m-0 flex list-none flex-col items-center gap-2.5 p-0">
        {layers.map((layer) => (
          <li key={layer.key} className={cn(layer.shape, "px-5 py-5 text-center")}>
            <p className={cn("m-0 text-[13px]", layer.key === "base" ? "font-semibold" : "text-gold")}>{layer.label}</p>
            <p className="m-0 mt-1.5 font-display text-lg leading-snug sm:text-xl">{list(layer.notes)}</p>
          </li>
        ))}
      </ol>
    </figure>
  );
}
