import type { Metadata } from "next";
import Link from "next/link";
import { PageHeading } from "@/components/PageHeading";
import { t } from "@/i18n";
import { listBrands, orEmpty } from "@/lib/catalog";

export const revalidate = 300;

export const metadata: Metadata = { title: t.brands.title, description: t.brands.intro, alternates: { canonical: "/markat" } };

export default async function BrandsPage() {
  const brands = await orEmpty(listBrands(), []);
  return (
    <>
      <PageHeading title={t.brands.title} intro={t.brands.intro} crumbs={[{ name: t.nav.home, href: "/" }, { name: t.brands.title }]} />
      <div className="mx-auto max-w-[1440px] px-5 pt-10 pb-20 md:px-14">
        {brands.length === 0 ? (
          <p className="text-muted">{t.brands.empty}</p>
        ) : (
          <ul className="m-0 grid list-none grid-cols-[repeat(auto-fill,minmax(min(260px,100%),1fr))] gap-5 p-0">
            {brands.map((b) => (
              <li key={b.slug}>
                <Link href={`/markat/${b.slug}`} className="card group flex min-h-[160px] flex-col justify-between rounded-[20px] border border-line bg-panel p-6 text-text hover:border-gold hover:text-text">
                  <span className="font-display text-[30px] leading-tight text-heading group-hover:text-gold-light">{b.name}</span>
                  <span className="text-sm text-subtle">{t.brands.count(b.fragranceCount)}</span>
                </Link>
              </li>
            ))}
          </ul>
        )}
      </div>
    </>
  );
}
