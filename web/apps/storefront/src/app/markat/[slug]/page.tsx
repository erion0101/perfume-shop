import type { Metadata } from "next";
import Link from "next/link";
import { notFound } from "next/navigation";
import { PageHeading } from "@/components/PageHeading";
import { ProductGrid } from "@/components/product/ProductCard";
import { t } from "@/i18n";
import { getBrand, listBrands, listFragrances, NotFoundError, orEmpty } from "@/lib/catalog";

export const revalidate = 300;

type Props = { params: Promise<{ slug: string }> };

export async function generateStaticParams() {
  return (await orEmpty(listBrands(), [])).map((b) => ({ slug: b.slug }));
}

async function load(slug: string) {
  try {
    return await getBrand(slug);
  } catch (error) {
    if (error instanceof NotFoundError) {
      notFound();
    }

    throw error;
  }
}

export async function generateMetadata({ params }: Props): Promise<Metadata> {
  const brand = await load((await params).slug);
  return {
    title: t.brands.perfumesBy(brand.name),
    description: brand.description?.slice(0, 160) ?? `${t.brands.perfumesBy(brand.name)}: ${t.brands.count(brand.fragranceCount)}. ${t.announcement.cod}`,
    alternates: { canonical: `/markat/${brand.slug}` },
  };
}

export default async function BrandPage({ params }: Props) {
  const brand = await load((await params).slug);
  // A brand page shows its whole range (at most a few dozen); filters live on /parfumet?marka=…
  const page = await listFragrances({ brand: brand.slug, sort: "name", pageSize: 48 });
  return (
    <>
      <PageHeading
        title={brand.name}
        intro={<p className="m-0">{brand.description ?? t.brands.descriptionMissing}</p>}
        crumbs={[{ name: t.nav.home, href: "/" }, { name: t.brands.title, href: "/markat" }, { name: brand.name }]}
      >
        <Link href={`/parfumet?marka=${brand.slug}`} className="inline-flex min-h-12 items-center rounded-full border border-line-strong px-6 text-text hover:border-gold hover:text-text">
          {t.listing.openFilters}
        </Link>
      </PageHeading>
      <section aria-label={t.brands.perfumesBy(brand.name)} className="mx-auto max-w-[1440px] px-5 pt-10 pb-20 md:px-14">
        <p className="m-0 mb-6 text-sm text-subtle">{t.brands.count(page.totalCount)}</p>
        <ProductGrid items={page.items} priorityCount={2} />
      </section>
    </>
  );
}
