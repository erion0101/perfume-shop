import { Button, cn } from "@perfumeshop/ui";
import {
  Banknote,
  BarChart3,
  Boxes,
  ClipboardList,
  Download,
  FileSpreadsheet,
  FlaskConical,
  HandCoins,
  LayoutDashboard,
  ListChecks,
  LogOut,
  Menu,
  Package,
  PackagePlus,
  Receipt,
  ScanSearch,
  Tags,
  Truck,
  Wallet,
  X,
} from "lucide-react";
import { useState, type ComponentType } from "react";
import { NavLink, Outlet, useNavigate } from "react-router";
import { PendingWatcher } from "@/features/orders/PendingWatcher";
import { t } from "@/i18n";
import { useAuth } from "@/lib/auth";

interface NavItem {
  to: string;
  label: string;
  icon: ComponentType<{ className?: string }>;
  end?: boolean;
  adminOnly?: boolean;
}

const sections: { title?: string; items: NavItem[] }[] = [
  { items: [{ to: "/", label: t.nav.dashboard, icon: LayoutDashboard, end: true }] },
  {
    title: t.nav.orders,
    items: [
      { to: "/orders/queue", label: t.nav.queue, icon: ListChecks },
      { to: "/orders", label: t.nav.orderList, icon: ClipboardList, end: true },
    ],
  },
  {
    title: t.nav.shipping,
    items: [
      { to: "/shipping/shipments", label: t.nav.shipments, icon: Package },
      { to: "/shipping/handover", label: t.nav.handover, icon: Truck },
      { to: "/shipping/status-import", label: t.nav.statusImport, icon: FileSpreadsheet },
      { to: "/shipping/settlements", label: t.nav.settlements, icon: HandCoins },
      { to: "/shipping/reports", label: t.nav.reports, icon: BarChart3 },
      { to: "/shipping/couriers", label: t.nav.couriers, icon: Truck },
    ],
  },
  {
    title: t.nav.payments,
    items: [
      { to: "/payments", label: t.nav.paymentList, icon: Wallet, end: true },
      { to: "/payments/daily", label: t.nav.dailyCash, icon: Banknote },
      { to: "/payments/receipts", label: t.nav.receipts, icon: Receipt },
    ],
  },
  {
    title: t.nav.catalog,
    items: [
      { to: "/catalog/fragrances", label: t.nav.fragrances, icon: FlaskConical, adminOnly: true },
      { to: "/catalog/import", label: t.nav.importFragella, icon: Download, adminOnly: true },
      { to: "/catalog/brands", label: t.nav.brands, icon: Tags, adminOnly: true },
    ],
  },
  {
    title: t.nav.inventory,
    items: [
      { to: "/inventory", label: t.nav.stock, icon: Boxes, end: true },
      { to: "/inventory/receive", label: t.nav.receive, icon: PackagePlus },
      { to: "/inventory/trace", label: t.nav.batchTrace, icon: ScanSearch },
    ],
  },
];

export function AdminLayout() {
  const { session, isAdmin, signOut } = useAuth();
  const navigate = useNavigate();
  const [menuOpen, setMenuOpen] = useState(false);
  const visible = sections
    .map((section) => ({ ...section, items: section.items.filter((item) => !item.adminOnly || isAdmin) }))
    .filter((section) => section.items.length > 0);

  const nav = (
    <nav className="grid gap-4" aria-label={t.appName}>
      {visible.map((section, index) => (
        <div key={section.title ?? index} className="grid gap-1">
          {section.title ? <p className="px-3 text-xs font-semibold uppercase tracking-wide text-muted-foreground">{section.title}</p> : null}
          {section.items.map((item) => (
            <NavLink
              key={item.to}
              to={item.to}
              end={item.end}
              onClick={() => setMenuOpen(false)}
              className={({ isActive }) =>
                cn("flex items-center gap-2 rounded-md px-3 py-2.5 text-sm", isActive ? "bg-primary text-primary-foreground" : "hover:bg-muted")
              }
            >
              <item.icon className="size-4" />
              {item.label}
            </NavLink>
          ))}
        </div>
      ))}
    </nav>
  );

  return (
    <div className="min-h-dvh bg-muted/40 font-sans text-foreground md:flex">
      <aside className="hidden w-60 shrink-0 border-r border-border bg-background p-4 md:block">
        <p className="mb-6 px-3 text-lg font-semibold">PerfumeShop</p>
        {nav}
      </aside>

      {menuOpen ? (
        <div className="fixed inset-0 z-40 md:hidden" role="dialog" aria-modal="true" aria-label={t.nav.openMenu}>
          <button type="button" className="absolute inset-0 bg-black/40" aria-label={t.nav.closeMenu} onClick={() => setMenuOpen(false)} />
          <aside className="relative h-full w-72 max-w-[85%] overflow-y-auto bg-background p-4 shadow-xl">
            <div className="mb-6 flex items-center justify-between">
              <p className="px-3 text-lg font-semibold">PerfumeShop</p>
              <Button variant="ghost" size="icon" aria-label={t.nav.closeMenu} onClick={() => setMenuOpen(false)}>
                <X />
              </Button>
            </div>
            {nav}
          </aside>
        </div>
      ) : null}

      <div className="flex min-w-0 flex-1 flex-col">
        <header className="sticky top-0 z-30 flex h-14 items-center gap-2 border-b border-border bg-background px-3 sm:px-6">
          <Button variant="ghost" size="icon" className="md:hidden" aria-label={t.nav.openMenu} onClick={() => setMenuOpen(true)}>
            <Menu />
          </Button>
          <p className="font-semibold md:hidden">PerfumeShop</p>
          <div className="ml-auto flex items-center gap-2 text-sm sm:gap-3">
            <PendingWatcher />
            <span className="hidden text-right sm:block">
              <span className="block font-medium">{session?.email}</span>
              <span className="block text-xs text-muted-foreground">{isAdmin ? t.auth.roleAdmin : t.auth.roleStaff}</span>
            </span>
            <Button
              variant="outline"
              size="sm"
              onClick={() => {
                void signOut().then(() => navigate("/login", { replace: true }));
              }}
            >
              <LogOut /> {t.auth.signOut}
            </Button>
          </div>
        </header>
        <main className="flex-1 p-3 sm:p-6">
          <Outlet />
        </main>
      </div>
    </div>
  );
}
