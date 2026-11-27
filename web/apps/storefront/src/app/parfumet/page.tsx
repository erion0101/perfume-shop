import type { Metadata } from "next";
import { PageHeading } from "@/components/PageHeading";
import { Listing } from "@/features/listing/Listing";
import { t } from "@/i18n";
import { hasFilters, parseFilters, toSearch } from "@/lib/listing-params";

type Props = { searchParams: Promise<Record<string, string | string[] | undefined>> };

export async function generateMetadata({ searchParams }: Props): Promise<Metadata> {
  const filters = parseFilters(await searchParams);
  const page = filters.page > 1 ? `?${toSearch({ page: filters.page })}` : "";
  return {
    title: t.listing.title,
    description: t.listing.intro,
    // Filter combinations are not separate pages for search engines: canonical is the plain listing (with its page).
    alternates: { canonical: `/parfumet${page}` },
    robots: hasFilters(filters) || filters.sort ? { index: false, follow: true } : undefined,
  };
}

export default async function ParfumetPage({ searchParams }: Props) {
  const filters = parseFilters(await searchParams);
  return (
    <>
      <PageHeading title={t.listing.title} intro={t.listing.intro} crumbs={[{ name: t.nav.home, href: "/" }, { name: t.listing.title }]} />
      <Listing filters={filters} basePath="/parfumet" />
    </>
  );
}
