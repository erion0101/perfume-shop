import Link from "next/link";
import { t } from "@/i18n";
import { LogoMark } from "../Logo";

const columns = [
  {
    title: t.footer.shop,
    links: [
      { href: "/parfumet", label: t.nav.perfumes },
      { href: "/markat", label: t.nav.brands },
      { href: "/gjej-aromen", label: t.nav.quiz },
      { href: "/origjinaliteti", label: t.nav.authenticity },
    ],
  },
  {
    title: t.footer.help,
    links: [
      { href: "/dergesa-dhe-kthimet", label: t.footer.delivery },
      { href: "/pyetje-te-shpeshta", label: t.footer.faq },
      { href: "/kontakt", label: t.footer.contact },
      { href: "/rreth-nesh", label: t.footer.about },
    ],
  },
  {
    title: t.footer.legal,
    links: [
      { href: "/privatesia", label: t.footer.privacy },
      { href: "/kushtet", label: t.footer.terms },
    ],
  },
];

export function Footer() {
  return (
    <footer className="border-t border-line px-5 py-14 text-sm text-subtle md:px-14">
      <div className="mx-auto grid max-w-[1328px] gap-10 md:grid-cols-[1.4fr_repeat(3,1fr)]">
        <div className="grid content-start gap-3">
          <span className="flex items-center gap-3 font-display text-[22px] text-text">
            <LogoMark size={28} /> {t.siteName}
          </span>
          <p className="m-0">
            {t.footer.address} · {t.footer.phone} · {t.footer.social}
          </p>
          <p className="m-0 text-soft">{t.footer.cod}</p>
        </div>
        {columns.map((column) => (
          <nav key={column.title} aria-label={column.title}>
            <p className="m-0 mb-3 font-medium text-soft">{column.title}</p>
            <ul className="m-0 grid list-none gap-1 p-0">
              {column.links.map((link) => (
                <li key={link.href}>
                  <Link href={link.href} className="inline-flex min-h-11 items-center text-subtle hover:text-gold-light">
                    {link.label}
                  </Link>
                </li>
              ))}
            </ul>
          </nav>
        ))}
      </div>
      <p className="mx-auto mt-10 max-w-[1328px]">{t.footer.rights(new Date().getFullYear())}</p>
    </footer>
  );
}
