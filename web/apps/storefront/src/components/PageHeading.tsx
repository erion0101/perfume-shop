import Link from "next/link";
import type { ReactNode } from "react";
import { t } from "@/i18n";

export type Crumb = { name: string; href?: string };

export function Breadcrumbs({ items }: { items: Crumb[] }) {
  return (
    <nav aria-label={t.product.breadcrumb} className="text-sm text-subtle">
      <ol className="m-0 flex list-none flex-wrap items-center gap-2 p-0">
        {items.map((c, i) => (
          <li key={`${c.name}-${i}`} className="flex items-center gap-2">
            {i > 0 ? <span aria-hidden="true">/</span> : null}
            {c.href && i < items.length - 1 ? (
              <Link href={c.href} className="inline-flex min-h-11 items-center text-subtle hover:text-gold-light">
                {c.name}
              </Link>
            ) : (
              <span aria-current={i === items.length - 1 ? "page" : undefined} className="text-soft">
                {c.name}
              </span>
            )}
          </li>
        ))}
      </ol>
    </nav>
  );
}

export function PageHeading({ title, intro, crumbs, children }: { title: string; intro?: ReactNode; crumbs?: Crumb[]; children?: ReactNode }) {
  return (
    <div className="mx-auto max-w-[1440px] px-5 pt-6 md:px-14 md:pt-10">
      {crumbs ? <Breadcrumbs items={crumbs} /> : null}
      <div className="mt-4 flex flex-wrap items-end justify-between gap-6">
        <div className="max-w-[720px]">
          <h1 className="m-0 text-[40px] leading-[1.06] md:text-[60px]">{title}</h1>
          {intro ? <div className="m-0 mt-4 text-[17px] text-muted">{intro}</div> : null}
        </div>
        {children}
      </div>
    </div>
  );
}
