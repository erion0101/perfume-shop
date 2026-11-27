"use client";

import Link from "next/link";
import { useRef, useState } from "react";
import { ProductGrid } from "@/components/product/ProductCard";
import { label, t } from "@/i18n";
import type { FragranceCard, FragrancePage } from "@/lib/catalog";

const moods = ["Oud", "Sweet", "Fresh", "Spicy", "Floral"] as const;

/**
 * "Çfarë të tërheq më shumë?": pressing a direction loads its fragrances (same-origin API call); "Të gjitha" shows
 * the server-rendered newest ones. Moods with nothing to sell are hidden.
 */
export function MoodFilter({ initial, total, available, pageSize }: { initial: FragranceCard[]; total: number; available: string[]; pageSize: number }) {
  const [mood, setMood] = useState<string>("all");
  const [state, setState] = useState<{ items: FragranceCard[]; total: number; loading: boolean; error: boolean }>({ items: initial, total, loading: false, error: false });
  const request = useRef(0);

  async function pick(next: string) {
    setMood(next);
    if (next === "all") {
      setState({ items: initial, total, loading: false, error: false });
      return;
    }

    const id = ++request.current;
    setState((s) => ({ ...s, loading: true, error: false }));
    try {
      const response = await fetch(`/api/catalog/fragrances?Mood=${next}&PageSize=${pageSize}&Sort=newest`, { headers: { Accept: "application/json" } });
      if (!response.ok) {
        throw new Error(String(response.status));
      }

      const page = (await response.json()) as FragrancePage;
      if (id === request.current) {
        setState({ items: page.items, total: page.totalCount, loading: false, error: false });
      }
    } catch {
      if (id === request.current) {
        setState((s) => ({ ...s, loading: false, error: true }));
      }
    }
  }

  const shown = moods.filter((m) => available.includes(m));
  const listingHref = mood === "all" ? "/parfumet" : `/parfumet?drejtimi=${mood}`;

  return (
    <>
      <section id="gjej" aria-labelledby="mood-title" className="mx-auto max-w-[1440px] px-5 pt-16 pb-10 md:px-14 md:pt-24">
        <div className="flex flex-wrap items-end justify-between gap-6">
          <div className="max-w-[620px]">
            <h2 id="mood-title" className="m-0 text-[38px] leading-[1.08] md:text-[52px]">
              {t.home.moodTitle}
            </h2>
            <p className="m-0 mt-4 text-[17px] text-muted">{t.home.moodText}</p>
          </div>
          <p className="m-0 text-sm text-subtle" aria-live="polite">
            {state.loading ? t.common.loading : t.home.count(state.total)}
          </p>
        </div>
        <div role="group" aria-label={t.home.moodGroup} className="mt-9 flex flex-wrap gap-3">
          {["all", ...shown].map((m) => (
            <button
              key={m}
              type="button"
              aria-pressed={mood === m}
              onClick={() => void pick(m)}
              className="chip min-h-12 rounded-full border border-line-strong bg-transparent px-[22px] text-[15px] text-text hover:border-gold"
            >
              {m === "all" ? t.home.allMoods : label(t.enums.mood, m)}
            </button>
          ))}
        </div>
      </section>
      <section id="parfumet" aria-label={t.listing.title} className="mx-auto max-w-[1440px] px-5 pt-6 pb-20 md:px-14 md:pb-[104px]">
        {state.error ? (
          <p role="alert" className="text-danger">
            {t.common.error}{" "}
            <button type="button" className="underline" onClick={() => void pick(mood)}>
              {t.common.retry}
            </button>
          </p>
        ) : null}
        <div aria-busy={state.loading} className={state.loading ? "opacity-60 transition-opacity" : undefined}>
          <ProductGrid items={state.items} priorityCount={2} />
        </div>
        <div className="mt-10 flex justify-center">
          <Link href={listingHref} className="inline-flex min-h-12 items-center rounded-full border border-line-strong px-[30px] font-medium text-text hover:border-gold hover:text-text">
            {t.home.seeAll}
          </Link>
        </div>
      </section>
    </>
  );
}
