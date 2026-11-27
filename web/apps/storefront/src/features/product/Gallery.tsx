"use client";

import { cn } from "@perfumeshop/ui";
import Image from "next/image";
import { useState } from "react";
import { Bottle } from "@/components/Bottle";
import { t } from "@/i18n";

type Img = { url: string; altText: string | null; sortOrder: number };

/** Photos with thumbnails; without photos, the drawn bottle in the arch (labelled for screen readers). */
export function Gallery({ images, name, slug, mood }: { images: Img[]; name: string; slug: string; mood: string | null | undefined }) {
  const sorted = [...images].sort((a, b) => a.sortOrder - b.sortOrder);
  const [index, setIndex] = useState(0);
  const current = sorted[index];
  const frame = "relative grid aspect-[4/5] w-full place-items-center overflow-hidden rounded-[240px_240px_18px_18px] border border-[#3A2F22] bg-[radial-gradient(120%_70%_at_50%_8%,#3A2A16_0%,#1A130C_45%,#0C0A08_100%)]";

  if (!current) {
    return (
      <div className={cn(frame, "card")}>
        <div className="absolute top-[12%] left-1/2 -ml-9 size-[72px] rounded-full bg-[radial-gradient(circle_at_40%_40%,#F6E4B4,#C8A15A_70%)] opacity-90 shadow-[0_0_80px_26px_rgba(200,161,90,.22)]" aria-hidden="true" />
        <Bottle size="product" mood={mood} slug={slug} label={name.toUpperCase()} title={t.product.bottleLabel(name)} className="mt-16" />
      </div>
    );
  }

  return (
    <div className="grid gap-4" role="group" aria-label={t.product.gallery(name)}>
      <div className={frame}>
        <Image src={current.url} alt={current.altText ?? t.product.photo(name, index + 1)} fill sizes="(max-width: 768px) 100vw, 560px" priority className="object-contain p-8" />
      </div>
      {sorted.length > 1 ? (
        <ul className="m-0 flex list-none gap-3 p-0">
          {sorted.map((img, i) => (
            <li key={img.url}>
              <button type="button" onClick={() => setIndex(i)} aria-label={t.product.showPhoto(i + 1)} aria-pressed={i === index} className={cn("relative size-[72px] overflow-hidden rounded-xl border bg-panel", i === index ? "border-gold" : "border-line")}>
                <Image src={img.url} alt="" fill sizes="72px" className="object-contain p-1.5" />
              </button>
            </li>
          ))}
        </ul>
      ) : null}
    </div>
  );
}
