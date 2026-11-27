import Link from "next/link";
import { CartButton } from "@/features/cart/CartDrawer";
import { t } from "@/i18n";
import { LogoMark } from "../Logo";
import { MobileMenu } from "./MobileMenu";
import { NavLinks } from "./NavLinks";

export const mainNav = [
  { href: "/parfumet", label: t.nav.perfumes },
  { href: "/markat", label: t.nav.brands },
  { href: "/gjej-aromen", label: t.nav.quiz },
  { href: "/origjinaliteti", label: t.nav.authenticity },
];

export function Header() {
  return (
    <header className="sticky top-0 z-40 border-b border-line bg-ground/95 backdrop-blur supports-[backdrop-filter]:bg-ground/85">
      <div className="mx-auto flex max-w-[1440px] items-center justify-between gap-4 px-5 py-3.5 md:gap-6 md:px-14 md:py-[22px]">
        <div className="flex items-center gap-1">
          <MobileMenu items={mainNav} />
          <Link href="/" className="flex items-center gap-3 text-text hover:text-text" aria-label={`${t.siteName}, ${t.nav.home}`}>
            <LogoMark />
            <span className="font-display text-[21px] tracking-[0.04em] sm:text-2xl">{t.siteName}</span>
          </Link>
        </div>
        <NavLinks items={mainNav} />
        <div className="flex gap-2.5">
          <Link href="/kerko" aria-label={t.nav.search} className="grid size-11 place-items-center rounded-full border border-line text-text hover:border-gold hover:text-text">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" aria-hidden="true">
              <circle cx="11" cy="11" r="7" />
              <path d="m20 20-3.5-3.5" />
            </svg>
          </Link>
          <CartButton />
        </div>
      </div>
    </header>
  );
}
