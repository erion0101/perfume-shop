"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useState } from "react";
import { Drawer } from "@/components/Drawer";
import { t } from "@/i18n";

/** Phones and tablets: the navigation slides in from the left. */
export function MobileMenu({ items }: { items: { href: string; label: string }[] }) {
  const [open, setOpen] = useState(false);
  const pathname = usePathname();

  return (
    <div className="md:hidden">
      <button type="button" onClick={() => setOpen(true)} aria-label={t.nav.openMenu} aria-expanded={open} className="-ml-2 grid size-11 place-items-center rounded-full text-text">
        <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" aria-hidden="true">
          <path d="M4 7h16M4 12h16M4 17h10" />
        </svg>
      </button>
      <Drawer open={open} onClose={() => setOpen(false)} title={t.nav.menu} side="left" closeLabel={t.nav.closeMenu}>
        <nav aria-label={t.nav.main}>
          <ul className="m-0 grid list-none gap-1 p-0">
            {[{ href: "/", label: t.nav.home }, ...items, { href: "/kerko", label: t.nav.search }].map((item) => (
              <li key={item.href}>
                <Link
                  href={item.href}
                  aria-current={pathname === item.href ? "page" : undefined}
                  className="flex min-h-12 items-center border-b border-line font-display text-2xl text-text aria-[current=page]:text-gold-light"
                  onClick={() => setOpen(false)}
                >
                  {item.label}
                </Link>
              </li>
            ))}
          </ul>
        </nav>
      </Drawer>
    </div>
  );
}
