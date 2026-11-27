import Image from "next/image";
import Link from "next/link";
import { accordName, capitalize, formatSize, t } from "@/i18n";
import type { FragranceCard } from "@/lib/catalog";
import { Bottle } from "../Bottle";
import { Price } from "../Price";
import { AddToCartButton } from "./AddToCartButton";

/** The card of the approved design: photo (or drawn bottle), brand, name, notes, size · price and "Shto". */
export function ProductCard({ fragrance, priority = false }: { fragrance: FragranceCard; priority?: boolean }) {
  const v = fragrance.defaultVariant;
  const size = formatSize(v.size);
  const href = `/parfum/${fragrance.slug}`;
  const accords = fragrance.topAccords.map((a) => capitalize(accordName(a.name)));
  return (
    <article className="card relative flex flex-col rounded-[20px] border border-line bg-panel p-[22px]">
      <div className="flex h-[250px] items-end justify-center overflow-hidden rounded-[14px] bg-[radial-gradient(90%_80%_at_50%_30%,#2A1F14,#15110D_75%)] pb-[26px]">
        {fragrance.image ? (
          <Image
            src={fragrance.image.url}
            alt={fragrance.image.altText ?? `${fragrance.brandName} ${fragrance.name}`}
            width={240}
            height={240}
            sizes="(max-width: 640px) 80vw, 240px"
            priority={priority}
            className="bottle h-[220px] w-auto object-contain"
          />
        ) : (
          <Bottle mood={fragrance.mood} slug={fragrance.slug} />
        )}
      </div>
      <p className="m-0 mt-5 mb-1 text-[13px] text-gold">{fragrance.brandName}</p>
      <h3 className="m-0 text-[26px] leading-tight">
        {/* The whole card is clickable through this link; "Shto" stays a separate button. */}
        <Link href={href} className="text-heading after:absolute after:inset-0 after:rounded-[20px] after:content-[''] hover:text-gold-light">
          {fragrance.name}
        </Link>
      </h3>
      {accords.length > 0 ? <p className="m-0 mt-2 text-sm text-muted">{accords.join(", ")}</p> : null}
      <div className="mt-auto flex items-center justify-between gap-3 pt-[22px]">
        <span className="text-[17px]">
          {size && fragrance.variantCount === 1 ? <span className="text-text">{size} · </span> : null}
          <Price amount={v.price.amount} compareAt={v.compareAtPrice?.amount} from={fragrance.variantCount > 1} />
        </span>
        <div className="relative z-10">
          {v.inStock && fragrance.variantCount === 1 ? (
            <AddToCartButton variantId={v.id} slug={fragrance.slug} name={fragrance.name} compact />
          ) : v.inStock ? (
            <Link href={href} className="inline-flex min-h-11 items-center whitespace-nowrap rounded-full border border-gold px-[18px] text-sm font-medium text-gold-light" aria-label={`${t.cart.chooseSize}: ${fragrance.name}`}>
              {t.cart.choose}
            </Link>
          ) : (
            <span className="text-sm text-subtle">{t.product.outOfStock}</span>
          )}
        </div>
      </div>
    </article>
  );
}

export function ProductGrid({ items, priorityCount = 0 }: { items: FragranceCard[]; priorityCount?: number }) {
  return (
    <ul className="m-0 grid list-none grid-cols-[repeat(auto-fill,minmax(min(280px,100%),1fr))] gap-6 p-0">
      {items.map((f, i) => (
        <li key={f.id} className="contents">
          <ProductCard fragrance={f} priority={i < priorityCount} />
        </li>
      ))}
    </ul>
  );
}
