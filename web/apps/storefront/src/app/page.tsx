import Link from "next/link";
import { Bottle } from "@/components/Bottle";
import { ProfileMeters } from "@/components/Meters";
import { NotePyramid } from "@/components/NotePyramid";
import { MoodFilter } from "@/features/home/MoodFilter";
import { t } from "@/i18n";
import { getFacets, getFragrance, listBrands, listFragrances, orEmpty, type FragranceDetails } from "@/lib/catalog";

export const revalidate = 300;

const GRID_SIZE = 6;

/** "Aroma e javës": FEATURED_FRAGRANCE_SLUG when set, otherwise the newest fragrance with a note pyramid. */
async function weekly(candidates: string[]): Promise<FragranceDetails | null> {
  const configured = process.env.FEATURED_FRAGRANCE_SLUG;
  for (const slug of configured ? [configured, ...candidates] : candidates.slice(0, 4)) {
    const details = await getFragrance(slug).catch(() => null);
    if (details && details.notes.top.length + details.notes.heart.length + details.notes.base.length > 0) {
      return details;
    }
  }

  return null;
}

const trust = [
  { text: t.home.trustAuthentic, icon: <><path d="M12 3 4 6v6c0 5 3.5 8 8 9 4.5-1 8-4 8-9V6z" /><path d="m9 12 2 2 4-4" /></> },
  { text: t.home.trustCod, icon: <><rect x="3" y="6" width="18" height="12" rx="2" /><circle cx="12" cy="12" r="2.5" /></> },
  { text: t.home.trustOpen, icon: <><path d="M3 7h11v9H3zM14 10h4l3 3v3h-7" /><circle cx="7" cy="18" r="1.6" /><circle cx="17" cy="18" r="1.6" /></> },
];

export default async function HomePage() {
  const [page, brands, facets] = await Promise.all([
    orEmpty(listFragrances({ sort: "newest", pageSize: GRID_SIZE }), { items: [], page: 1, pageSize: GRID_SIZE, totalCount: 0 }),
    orEmpty(listBrands(), []),
    orEmpty(getFacets(), null),
  ]);
  const week = await weekly(page.items.map((i) => i.slug));
  const heroName = (week?.name ?? page.items[0]?.name ?? "Khamrah").toUpperCase();

  return (
    <>
      <section aria-labelledby="hero-title" className="mx-auto box-border flex max-w-[1440px] flex-wrap items-center gap-12 px-5 pt-12 pb-16 md:gap-14 md:px-14 md:pt-[72px] md:pb-[88px]">
        <div className="min-w-0 flex-[1_1_520px]">
          <p className="m-0 mb-[22px] text-[15px] text-gold">{t.tagline}</p>
          <h1 id="hero-title" className="m-0 text-[46px] leading-[1.02] tracking-[-0.01em] sm:text-[64px] lg:text-[86px]">
            {t.home.heroTitle}
          </h1>
          <p className="m-0 mt-7 max-w-[500px] text-lg text-muted">{t.home.heroText}</p>
          <div className="mt-10 flex flex-wrap gap-3.5">
            <Link href="/parfumet" className="inline-flex min-h-[52px] items-center rounded-full bg-gold px-[30px] font-semibold text-ground hover:bg-gold-light hover:text-ground">
              {t.home.heroPrimary}
            </Link>
            <Link href="/gjej-aromen" className="inline-flex min-h-[52px] items-center rounded-full border border-line-strong px-[30px] font-medium text-text hover:border-gold hover:text-text">
              {t.home.heroSecondary}
            </Link>
          </div>
        </div>
        <div className="flex min-w-0 flex-[1_1_440px] justify-center">
          {/* The arched "desert night" window: moon, three dunes, a bottle in the sand. Decorative. */}
          <div aria-hidden="true" className="card relative aspect-[4/5] w-full max-w-[480px] overflow-hidden rounded-[240px_240px_18px_18px] border border-[#3A2F22] bg-[radial-gradient(120%_70%_at_50%_8%,#3A2A16_0%,#1A130C_45%,#0C0A08_100%)]">
            <div className="absolute top-[13%] left-1/2 -ml-[42px] size-[84px] rounded-full bg-[radial-gradient(circle_at_40%_40%,#F6E4B4,#C8A15A_70%)] shadow-[0_0_90px_30px_rgba(200,161,90,.28)]" />
            <svg viewBox="0 0 480 600" preserveAspectRatio="none" className="absolute inset-0 size-full">
              <path d="M0 430 C110 380 170 400 250 440 S400 470 480 420 V600 H0Z" fill="#20180F" />
              <path d="M0 490 C140 450 230 470 320 505 S440 520 480 500 V600 H0Z" fill="#2B2014" />
              <path d="M0 545 C120 525 260 535 360 560 S460 570 480 565 V600 H0Z" fill="#0C0A08" />
            </svg>
            <div className="absolute bottom-[16%] left-1/2 -translate-x-1/2">
              <Bottle size="hero" mood={week?.mood ?? "Sweet"} slug={week?.slug ?? "hero"} label={heroName} />
            </div>
          </div>
        </div>
      </section>

      <section aria-label={t.siteName} className="border-y border-line px-5 py-[26px] md:px-14">
        <ul className="mx-auto m-0 grid max-w-[1328px] list-none grid-cols-[repeat(auto-fit,minmax(240px,1fr))] gap-5 p-0 text-[15px] text-soft">
          {trust.map((item) => (
            <li key={item.text} className="flex items-center gap-3">
              <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="#C8A15A" strokeWidth="1.6" aria-hidden="true">
                {item.icon}
              </svg>
              {item.text}
            </li>
          ))}
        </ul>
      </section>

      <MoodFilter initial={page.items} total={page.totalCount} available={(facets?.moods ?? []).map((m) => m.value)} pageSize={GRID_SIZE} />

      {week ? (
        <section aria-labelledby="week-title" className="border-y border-line bg-panel px-5 py-20 md:px-14 md:py-[104px]">
          <div className="mx-auto flex max-w-[1328px] flex-wrap items-center gap-16">
            <div className="min-w-0 flex-[1_1_460px]">
              <p className="m-0 mb-3.5 text-[15px] text-gold">{t.home.weekLabel}</p>
              <h2 id="week-title" className="m-0 text-[42px] leading-[1.04] md:text-[60px]">
                {week.brand.name} {week.name}
              </h2>
              {week.description ? <p className="m-0 mt-[18px] line-clamp-4 max-w-[470px] text-[17px] text-muted">{week.description}</p> : null}
              <ProfileMeters longevity={week.longevity} sillage={week.sillage} className="mt-9 grid max-w-[440px] gap-[18px]" />
              <Link href={`/parfum/${week.slug}`} className="mt-10 inline-flex min-h-[52px] items-center rounded-full bg-gold px-[30px] font-semibold text-ground hover:bg-gold-light hover:text-ground">
                {t.home.weekCta(week.name)}
              </Link>
            </div>
            <NotePyramid notes={week.notes} className="min-w-0 flex-[1_1_460px]" />
          </div>
        </section>
      ) : null}

      <section id="origjinale" aria-labelledby="auth-title" className="mx-auto box-border max-w-[1440px] px-5 py-20 md:px-14 md:py-[104px]">
        <div className="flex flex-wrap items-center gap-14">
          <div className="min-w-0 flex-[1_1_480px]">
            <h2 id="auth-title" className="m-0 text-[38px] leading-[1.08] md:text-[52px]">
              {t.home.authTitle}
            </h2>
            <p className="m-0 mt-[18px] max-w-[520px] text-[17px] text-muted">{t.home.authText}</p>
          </div>
          <form action="/origjinaliteti" method="get" className="min-w-0 flex-[1_1_420px] rounded-[20px] border border-line bg-panel p-6 sm:p-8">
            <label htmlFor="batch" className="mb-2.5 block text-sm text-soft">
              {t.authenticity.code}
            </label>
            <div className="flex flex-wrap gap-2.5">
              <input
                id="batch"
                name="kodi"
                required
                autoComplete="off"
                autoCapitalize="characters"
                spellCheck={false}
                maxLength={50}
                placeholder="p.sh. LT2409A"
                className="min-h-[52px] min-w-0 flex-[1_1_200px] rounded-xl border border-field bg-ground px-[18px] text-base text-text placeholder:text-subtle"
              />
              <button type="submit" className="min-h-[52px] rounded-xl bg-gold px-[26px] font-semibold text-ground hover:bg-gold-light">
                {t.authenticity.check}
              </button>
            </div>
          </form>
        </div>
      </section>

      {brands.length > 0 ? (
        <section id="markat" aria-labelledby="brands-title" className="border-t border-line px-5 py-14 md:px-14">
          <h2 id="brands-title" className="sr-only">
            {t.home.brandsTitle}
          </h2>
          <ul className="mx-auto m-0 flex max-w-[1328px] list-none flex-wrap justify-between gap-x-12 gap-y-7 p-0 font-display text-[28px]">
            {brands.map((b) => (
              <li key={b.slug}>
                <Link href={`/markat/${b.slug}`} className="text-subtle hover:text-gold-light">
                  {b.name}
                </Link>
              </li>
            ))}
          </ul>
        </section>
      ) : null}
    </>
  );
}
