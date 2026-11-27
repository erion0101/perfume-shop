import type { Metadata } from "next";
import { PageHeading } from "@/components/PageHeading";
import { Listing } from "@/features/listing/Listing";
import { SearchBox } from "@/features/search/SearchBox";
import { t } from "@/i18n";
import { parseFilters } from "@/lib/listing-params";

type Props = { searchParams: Promise<Record<string, string | string[] | undefined>> };

export async function generateMetadata({ searchParams }: Props): Promise<Metadata> {
  const { q } = parseFilters(await searchParams);
  return { title: q ? t.search.resultsFor(q) : t.search.title, robots: { index: false, follow: true }, alternates: { canonical: "/kerko" } };
}

export default async function SearchPage({ searchParams }: Props) {
  const filters = parseFilters(await searchParams);
  const q = filters.q.trim();
  return (
    <>
      <PageHeading title={t.search.title} crumbs={[{ name: t.nav.home, href: "/" }, { name: t.search.title }]} />
      <div className="mx-auto max-w-[1440px] px-5 pt-6 md:px-14">
        <div className="max-w-[760px]">
          <SearchBox initialQuery={q} autoFocus={!q} />
        </div>
        {q ? <h2 className="m-0 mt-12 text-[28px] md:text-[36px]">{t.search.resultsFor(q)}</h2> : <p className="mt-6 text-muted">{t.search.start}</p>}
      </div>
      {q.length >= 2 ? <Listing filters={filters} basePath="/kerko" /> : <div className="pb-24" />}
    </>
  );
}
