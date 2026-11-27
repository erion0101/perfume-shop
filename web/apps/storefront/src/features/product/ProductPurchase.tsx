"use client";

import { cn } from "@perfumeshop/ui";
import { useEffect, useRef, useState } from "react";
import { Price } from "@/components/Price";
import { AddToCartButton } from "@/components/product/AddToCartButton";
import { formatSize, label, t } from "@/i18n";
import type { FragranceDetails } from "@/lib/catalog";

type Variant = FragranceDetails["variants"][number];

export const variantLabel = (v: Variant) => [formatSize(v.size), v.type === "FullBottle" ? null : label(t.enums.variantType, v.type)].filter(Boolean).join(" · ") || label(t.enums.variantType, v.type);

/**
 * Size / type picker, price, stock and "Shto në shportë". On phones a sticky bar repeats the button while the main
 * one is scrolled out of view.
 */
export function ProductPurchase({ fragrance }: { fragrance: FragranceDetails }) {
  const variants = fragrance.variants;
  const [selectedId, setSelectedId] = useState(() => (variants.find((v) => v.inStock) ?? variants[0])?.id);
  const selected = variants.find((v) => v.id === selectedId) ?? variants[0];
  const button = useRef<HTMLDivElement>(null);
  const [mainVisible, setMainVisible] = useState(true);

  useEffect(() => {
    const element = button.current;
    if (!element || typeof IntersectionObserver === "undefined") {
      return;
    }

    const observer = new IntersectionObserver(([entry]) => setMainVisible(entry?.isIntersecting ?? true), { rootMargin: "0px 0px -40px 0px" });
    observer.observe(element);
    return () => observer.disconnect();
  }, []);

  if (!selected || !selected.price) {
    return null;
  }

  const name = `${fragrance.brand.name} ${fragrance.name}`;
  const inStock = selected.inStock === true;

  return (
    <div className="grid gap-6">
      {variants.length > 1 ? (
        <fieldset className="m-0 border-0 p-0">
          <legend className="mb-3 p-0 text-sm text-soft">{t.product.size}</legend>
          <div className="flex flex-wrap gap-2.5">
            {variants.map((v) => (
              <label
                key={v.id}
                className={cn(
                  "flex min-h-12 cursor-pointer items-center rounded-full border px-5 text-[15px] has-[:focus-visible]:outline-2 has-[:focus-visible]:outline-offset-3 has-[:focus-visible]:outline-gold-light",
                  v.id === selected.id ? "border-gold bg-gold text-ground" : "border-line-strong text-text hover:border-gold",
                  !v.inStock && v.id !== selected.id && "text-subtle line-through decoration-1",
                )}
              >
                <input type="radio" name="variant" value={v.id} checked={v.id === selected.id} onChange={() => setSelectedId(v.id)} className="sr-only" />
                {variantLabel(v)}
                {!v.inStock ? <span className="sr-only"> ({t.product.outOfStock})</span> : null}
              </label>
            ))}
          </div>
        </fieldset>
      ) : (
        <p className="m-0 text-[15px] text-soft">{variantLabel(selected)}</p>
      )}

      <div className="flex flex-wrap items-baseline justify-between gap-3">
        <Price amount={selected.price.amount} compareAt={selected.compareAtPrice?.amount} className="text-[28px]" />
        <p className={cn("m-0 flex items-center gap-2 text-sm", inStock ? "text-soft" : "text-danger")} role="status">
          <span aria-hidden="true" className={cn("size-2 rounded-full", inStock ? "bg-gold" : "bg-danger")} />
          {inStock ? t.product.inStock : t.product.outOfStock}
        </p>
      </div>
      {!inStock ? <p className="m-0 text-sm text-muted">{t.product.outOfStockHint}</p> : null}

      <div ref={button}>
        <AddToCartButton variantId={selected.id} slug={fragrance.slug} name={name} disabled={!inStock} />
      </div>
      <p className="m-0 text-sm text-subtle">{t.product.authenticity}</p>

      {/* Sticky bar: phones only, while the main button is out of view. */}
      <div
        className={cn(
          "fixed inset-x-0 bottom-0 z-30 border-t border-line bg-panel/95 px-5 pt-3 pb-[max(12px,env(safe-area-inset-bottom))] backdrop-blur transition-transform md:hidden",
          mainVisible ? "pointer-events-none translate-y-full" : "translate-y-0",
        )}
        inert={mainVisible}
      >
        <div className="flex items-center gap-4">
          <div className="min-w-0 flex-1">
            <p className="m-0 truncate font-display text-lg text-heading">{fragrance.name}</p>
            <p className="m-0 text-sm text-soft">{variantLabel(selected)}</p>
          </div>
          <div className="w-[52%]">
            <AddToCartButton variantId={selected.id} slug={fragrance.slug} name={name} disabled={!inStock} />
          </div>
        </div>
      </div>
    </div>
  );
}
