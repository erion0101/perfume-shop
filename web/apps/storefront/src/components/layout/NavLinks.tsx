"use client";

import { cn } from "@perfumeshop/ui";
import Link from "next/link";
import { usePathname } from "next/navigation";
import { t } from "@/i18n";

/** Desktop navigation; the current section gets aria-current and the gold colour. */
export function NavLinks({ items }: { items: { href: string; label: string }[] }) {
  const pathname = usePathname();
  return (
    <nav aria-label={t.nav.main} className="hidden md:block">
      <ul className="m-0 flex list-none gap-[34px] p-0 text-[15px]">
        {items.map((item) => {
          const active = pathname === item.href || pathname.startsWith(`${item.href}/`) || (item.href === "/parfumet" && pathname.startsWith("/parfum/"));
          return (
            <li key={item.href}>
              <Link href={item.href} aria-current={active ? "page" : undefined} className={cn("inline-flex min-h-11 items-center", active ? "text-gold-light" : "text-text hover:text-gold-light")}>
                {item.label}
              </Link>
            </li>
          );
        })}
      </ul>
    </nav>
  );
}
