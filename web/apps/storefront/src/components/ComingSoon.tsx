import Link from "next/link";
import { t } from "@/i18n";
import { PageHeading } from "./PageHeading";

/** Phase 2 pages linked from the header: a short note instead of a dead link. */
export function ComingSoon({ title, text }: { title: string; text: string }) {
  return (
    <>
      <PageHeading title={title} crumbs={[{ name: t.nav.home, href: "/" }, { name: title }]} />
      <div className="mx-auto max-w-[1440px] px-5 pt-8 pb-24 md:px-14">
        <p className="max-w-[560px] text-[17px] text-muted">{text}</p>
        <Link href="/parfumet" className="mt-6 inline-flex min-h-12 items-center rounded-full bg-gold px-[30px] font-semibold text-ground hover:bg-gold-light hover:text-ground">
          {t.home.heroPrimary}
        </Link>
      </div>
    </>
  );
}
