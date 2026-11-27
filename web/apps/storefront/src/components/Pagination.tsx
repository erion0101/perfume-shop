import Link from "next/link";
import { t } from "@/i18n";

/** Plain links (crawlable, work without JavaScript): previous, a window of pages, next. */
export function Pagination({ page, totalPages, href }: { page: number; totalPages: number; href: (page: number) => string }) {
  if (totalPages <= 1) {
    return null;
  }

  const windowStart = Math.max(1, Math.min(page - 2, totalPages - 4));
  const pages = Array.from({ length: Math.min(5, totalPages) }, (_, i) => windowStart + i);
  const item = "grid min-h-11 min-w-11 place-items-center rounded-full border px-3 tabular-nums";
  return (
    <nav aria-label={t.listing.pagination} className="mt-12 flex flex-wrap items-center justify-center gap-2">
      {page > 1 ? (
        <Link href={href(page - 1)} rel="prev" className={`${item} border-line-strong text-text hover:border-gold`} aria-label={t.listing.previous}>
          ‹
        </Link>
      ) : null}
      {pages.map((p) =>
        p === page ? (
          <span key={p} aria-current="page" className={`${item} border-gold bg-gold font-semibold text-ground`}>
            <span className="sr-only">{t.listing.pageOf(p, totalPages)}</span>
            <span aria-hidden="true">{p}</span>
          </span>
        ) : (
          <Link key={p} href={href(p)} className={`${item} border-line text-text hover:border-gold`} aria-label={t.listing.page(p)}>
            {p}
          </Link>
        ),
      )}
      {page < totalPages ? (
        <Link href={href(page + 1)} rel="next" className={`${item} border-line-strong text-text hover:border-gold`} aria-label={t.listing.next}>
          ›
        </Link>
      ) : null}
    </nav>
  );
}
