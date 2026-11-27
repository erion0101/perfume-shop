import type { Metadata } from "next";
import Link from "next/link";
import { notFound } from "next/navigation";
import { AccordBars, ProfileMeters } from "@/components/Meters";
import { NotePyramid } from "@/components/NotePyramid";
import { Breadcrumbs } from "@/components/PageHeading";
import { ProductGrid } from "@/components/product/ProductCard";
import { Gallery } from "@/features/product/Gallery";
import { ProductPurchase } from "@/features/product/ProductPurchase";
import { label, t } from "@/i18n";
import { getFragrance, getSimilar, listAllFragrances, NotFoundError, orEmpty } from "@/lib/catalog";

export const revalidate = 300;

type Props = { params: Promise<{ slug: string }> };

export async function generateStaticParams() {
  return (await orEmpty(listAllFragrances(), [])).map((f) => ({ slug: f.slug }));
}

async function load(slug: string) {
  try {
    return await getFragrance(slug);
  } catch (error) {
    if (error instanceof NotFoundError) {
      notFound();
    }

    throw error;
  }
}

export async function generateMetadata({ params }: Props): Promise<Metadata> {
  const f = await load((await params).slug);
  const title = `${f.brand.name} ${f.name} ${label(t.enums.concentration, f.concentration)}`;
  return {
    title,
    description: (f.description ?? `${title}. ${t.product.authenticity}`).slice(0, 160),
    alternates: { canonical: `/parfum/${f.slug}` },
  };
}

function Chips({ title, values, map }: { title: string; values: string[]; map: Record<string, string> }) {
  if (values.length === 0) {
    return null;
  }

  return (
    <div>
      <h3 className="m-0 mb-3 font-sans text-sm font-normal text-soft">{title}</h3>
      <ul className="m-0 flex list-none flex-wrap gap-2 p-0">
        {values.map((v) => (
          <li key={v} className="rounded-full border border-line-strong px-4 py-2 text-sm text-text">
            {label(map, v)}
          </li>
        ))}
      </ul>
    </div>
  );
}

export default async function ProductPage({ params }: Props) {
  const { slug } = await params;
  const f = await load(slug);
  const similar = await orEmpty(getSimilar(slug, 8), []);
  const images = f.variants.flatMap((v) => v.images).filter((img, i, all) => all.findIndex((x) => x.url === img.url) === i);
  const details: [string, string | null][] = [
    [t.product.concentration, label(t.enums.concentration, f.concentration)],
    [t.product.gender, label(t.enums.gender, f.gender)],
    [t.product.family, f.olfactiveFamily ? label(t.enums.family, f.olfactiveFamily) : null],
    [t.product.year, f.launchYear ? String(f.launchYear) : null],
    [t.product.perfumers, f.perfumers.length ? f.perfumers.join(", ") : null],
  ];

  return (
    <article className="pb-24 md:pb-0">
      <div className="mx-auto max-w-[1440px] px-5 pt-6 md:px-14 md:pt-10">
        <Breadcrumbs items={[{ name: t.nav.home, href: "/" }, { name: t.nav.perfumes, href: "/parfumet" }, { name: f.brand.name, href: `/markat/${f.brand.slug}` }, { name: f.name }]} />
      </div>

      <div className="mx-auto grid max-w-[1440px] gap-10 px-5 pt-6 pb-16 md:grid-cols-2 md:gap-16 md:px-14 md:pb-24">
        <div className="mx-auto w-full max-w-[520px] md:sticky md:top-28 md:self-start">
          <Gallery images={images} name={f.name} slug={f.slug} mood={f.mood} />
        </div>
        <div className="grid content-start gap-8">
          <div>
            <Link href={`/markat/${f.brand.slug}`} className="text-[15px] text-gold hover:text-gold-light">
              {f.brand.name}
            </Link>
            <h1 className="m-0 mt-2 text-[44px] leading-[1.04] md:text-[60px]">{f.name}</h1>
            <p className="m-0 mt-3 text-[15px] text-muted">
              {label(t.enums.concentration, f.concentration)} · {label(t.enums.gender, f.gender)}
              {f.mood ? ` · ${label(t.enums.mood, f.mood)}` : ""}
            </p>
          </div>
          <ProductPurchase fragrance={f} />
          <ProfileMeters longevity={f.longevity} sillage={f.sillage} />
        </div>
      </div>

      <section aria-labelledby="pyramid-title" className="border-y border-line bg-panel px-5 py-16 md:px-14 md:py-24">
        <div className="mx-auto grid max-w-[1328px] items-center gap-12 md:grid-cols-2 md:gap-16">
          <div className="grid gap-8">
            <div>
              <h2 id="pyramid-title" className="m-0 text-[36px] leading-[1.08] md:text-[48px]">
                {t.product.about}
              </h2>
              <p className="m-0 mt-4 max-w-[560px] whitespace-pre-line text-[17px] text-muted">{f.description ?? t.product.descriptionMissing}</p>
            </div>
            {f.accords.length > 0 ? (
              <div>
                <h3 className="m-0 mb-4 font-sans text-sm font-normal text-soft">{t.product.accords}</h3>
                <AccordBars accords={f.accords} />
              </div>
            ) : null}
            <Chips title={t.product.seasons} values={f.seasons} map={t.enums.season} />
            <Chips title={t.product.occasions} values={f.occasions} map={t.enums.occasion} />
          </div>
          <div>
            <h3 className="sr-only">{t.product.pyramid}</h3>
            <NotePyramid notes={f.notes} />
          </div>
        </div>
      </section>

      <section aria-labelledby="details-title" className="mx-auto max-w-[1440px] px-5 py-16 md:px-14">
        <h2 id="details-title" className="m-0 mb-6 text-[30px]">
          {t.product.details}
        </h2>
        <dl className="m-0 grid max-w-[720px] grid-cols-[minmax(8rem,12rem)_1fr] gap-x-6 gap-y-3 text-[15px]">
          {details
            .filter(([, value]) => value)
            .map(([name, value]) => (
              <div key={name} className="contents">
                <dt className="text-subtle">{name}</dt>
                <dd className="m-0 text-text">{value}</dd>
              </div>
            ))}
        </dl>
      </section>

      {similar.length > 0 ? (
        <section aria-labelledby="similar-title" className="mx-auto max-w-[1440px] border-t border-line px-5 pt-16 pb-20 md:px-14">
          <h2 id="similar-title" className="m-0 mb-8 text-[36px] md:text-[48px]">
            {t.product.similar}
          </h2>
          <ProductGrid items={similar.slice(0, 4)} />
        </section>
      ) : null}
    </article>
  );
}
