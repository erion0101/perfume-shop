"use client";

import { cn, formatMoney } from "@perfumeshop/ui";
import { useRouter } from "next/navigation";
import { useId, useRef, useState } from "react";
import { t } from "@/i18n";
import { MIN_QUERY, useSuggestions } from "./use-suggestions";

type Option = { id: string; href: string; primary: string; secondary: string; group: "perfumes" | "brands" };

/**
 * ARIA combobox: suggestions while typing (arrow keys + Enter pick one, Escape closes); Enter without a pick opens
 * the full results (/kerko?q=…). Works without JavaScript as a plain GET form.
 */
export function SearchBox({ initialQuery = "", autoFocus = false }: { initialQuery?: string; autoFocus?: boolean }) {
  const router = useRouter();
  const [query, setQuery] = useState(initialQuery);
  const [open, setOpen] = useState(false);
  const [active, setActive] = useState(-1);
  const listId = useId();
  const input = useRef<HTMLInputElement>(null);
  const { data, pending, error } = useSuggestions(query);

  const options: Option[] = [
    ...(data?.fragrances ?? []).map((f) => ({ id: `f-${f.id}`, href: `/parfum/${f.slug}`, primary: `${f.brandName} ${f.name}`, secondary: formatMoney(f.defaultVariant.price.amount), group: "perfumes" as const })),
    ...(data?.brands ?? []).map((b) => ({ id: `b-${b.slug}`, href: `/markat/${b.slug}`, primary: b.name, secondary: t.brands.count(b.fragranceCount), group: "brands" as const })),
  ];
  const showList = open && query.trim().length >= MIN_QUERY;
  const go = (href: string) => {
    setOpen(false);
    router.push(href);
  };

  return (
    <form
      role="search"
      action="/kerko"
      method="get"
      className="relative"
      onSubmit={(e) => {
        e.preventDefault();
        const picked = options[active];
        if (picked) {
          go(picked.href);
        } else if (query.trim()) {
          go(`/kerko?q=${encodeURIComponent(query.trim())}`);
        }
      }}
    >
      <label htmlFor={`${listId}-input`} className="mb-2.5 block text-sm text-soft">
        {t.search.label}
      </label>
      <div className="flex gap-2.5">
        <input
          ref={input}
          id={`${listId}-input`}
          name="q"
          type="search"
          role="combobox"
          aria-expanded={showList}
          aria-controls={listId}
          aria-autocomplete="list"
          aria-activedescendant={active >= 0 && options[active] ? `${listId}-${options[active].id}` : undefined}
          autoComplete="off"
          autoFocus={autoFocus}
          enterKeyHint="search"
          maxLength={100}
          placeholder={t.search.placeholder}
          value={query}
          onChange={(e) => {
            setQuery(e.target.value);
            setOpen(true);
            setActive(-1);
          }}
          onFocus={() => setOpen(true)}
          onBlur={() => window.setTimeout(() => setOpen(false), 120)}
          onKeyDown={(e) => {
            if (e.key === "ArrowDown") {
              e.preventDefault();
              setOpen(true);
              setActive((a) => Math.min(options.length - 1, a + 1));
            } else if (e.key === "ArrowUp") {
              e.preventDefault();
              setActive((a) => Math.max(-1, a - 1));
            } else if (e.key === "Escape") {
              setOpen(false);
              setActive(-1);
            }
          }}
          className="min-h-14 min-w-0 flex-1 rounded-full border border-field bg-ground px-6 text-lg text-text placeholder:text-subtle"
        />
        <button type="submit" className="min-h-14 rounded-full bg-gold px-7 font-semibold text-ground hover:bg-gold-light">
          {t.search.submit}
        </button>
      </div>

      <p className="sr-only" aria-live="polite">
        {showList && !pending ? (options.length ? t.search.count(options.length) : t.search.noSuggestions) : ""}
      </p>

      {showList ? (
        <div className="absolute inset-x-0 top-full z-20 mt-2 overflow-hidden rounded-[20px] border border-line bg-panel shadow-2xl">
          <ul id={listId} role="listbox" aria-label={t.search.suggestions} className="m-0 max-h-[60vh] list-none overflow-y-auto p-2">
            {options.map((o, i) => (
              <li
                key={o.id}
                id={`${listId}-${o.id}`}
                role="option"
                aria-selected={i === active}
                onMouseDown={(e) => {
                  e.preventDefault();
                  go(o.href);
                }}
                onMouseEnter={() => setActive(i)}
                className={cn("flex min-h-12 cursor-pointer items-center justify-between gap-4 rounded-xl px-4 py-2", i === active ? "bg-line text-heading" : "text-text")}
              >
                <span>
                  <span className="mr-2 text-xs uppercase tracking-[0.1em] text-subtle">{o.group === "brands" ? t.search.brands : t.search.perfumes}</span>
                  {o.primary}
                </span>
                <span className="text-sm text-muted">{o.secondary}</span>
              </li>
            ))}
          </ul>
          {options.length === 0 ? <p className="m-0 px-6 py-4 text-muted">{pending ? t.search.loading : error ? t.common.error : t.search.noSuggestions}</p> : null}
          {query.trim() ? (
            <button type="submit" className="block min-h-12 w-full border-t border-line px-6 text-left text-sm text-gold-light hover:bg-line">
              {t.search.seeAll(query.trim())}
            </button>
          ) : null}
        </div>
      ) : null}
    </form>
  );
}
