import { Button, cn } from "@perfumeshop/ui";
import {
  AlertTriangle,
  Banknote,
  BellRing,
  Building2,
  Gauge,
  History,
  KeyRound,
  MessageSquareText,
  ScrollText,
  ShieldCheck,
  Users,
  UserCog,
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
import { Link, NavLink, Outlet, useNavigate } from "react-router";
import { useBusinessProfile } from "@/features/admin/api";
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
    title: t.nav.customers,
    items: [{ to: "/customers", label: t.nav.customers, icon: Users }],
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
  {
    title: t.nav.notifications,
    items: [
      { to: "/notifications/templates", label: t.nav.templates, icon: MessageSquareText },
      { to: "/notifications/messages", label: t.nav.notificationLog, icon: ScrollText },
      { to: "/notifications/alerts", label: t.nav.staffAlerts, icon: BellRing },
    ],
  },
  {
    title: t.nav.administration,
    items: [
      { to: "/admin/staff", label: t.nav.staff, icon: UserCog, adminOnly: true },
      { to: "/admin/audit", label: t.nav.audit, icon: History, adminOnly: true },
      { to: "/settings/business", label: t.nav.business, icon: Building2 },
      { to: "/settings/delivery", label: t.nav.delivery, icon: ShieldCheck, adminOnly: true },
      { to: "/settings/fragella", label: t.nav.fragellaUsage, icon: Gauge, adminOnly: true },
      { to: "/account", label: t.nav.myAccount, icon: KeyRound },
    ],
  },
];

/** Shown on every page while the shop name, address, phone or fiscal number is still the seeded example. */
export function PlaceholderBanner() {
  const { isAdmin } = useAuth();
  const profile = useBusinessProfile();
  const fields = profile.data?.placeholderFields ?? [];
  if (fields.length === 0) {
    return null;
  }

  return (
    <div role="alert" className="flex flex-wrap items-center gap-2 border-b border-amber-300 bg-amber-50 px-3 py-2 text-sm text-amber-950 sm:px-6">
      <AlertTriangle className="size-4 shrink-0" aria-hidden />
      <span className="flex-1">{t.settings.placeholderBanner(fields.map((f) => t.settings.fieldNames[f] ?? f).join(", "))}</span>
      {isAdmin ? (
        <Link to="/settings/business" className="font-medium underline underline-offset-2">
          {t.settings.fixNow}
        </Link>
      ) : null}
    </div>
  );
}

/** Admins must turn on 2FA (Auth:Staff:RequireTwoFactorForAdmins); until then the API refuses admin pages. */
function TwoFactorBanner() {
  const { session } = useAuth();
  if (!session?.twoFactorSetupRequired) {
    return null;
  }

  return (
    <div role="alert" className="flex flex-wrap items-center gap-2 border-b border-red-300 bg-red-50 px-3 py-2 text-sm text-red-950 sm:px-6">
      <ShieldCheck className="size-4 shrink-0" aria-hidden />
      <span className="flex-1">{t.account.setupRequiredBanner}</span>
      <Link to="/account" className="font-medium underline underline-offset-2">
        {t.account.setupNow}
      </Link>
    </div>
  );
}

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
            <Link to="/account" className="hidden rounded-md px-2 py-1 text-right hover:bg-muted sm:block" aria-label={`${t.nav.myAccount}: ${session?.displayName || session?.email}`}>
              <span className="block font-medium">{session?.displayName || session?.email}</span>
              <span className="block text-xs text-muted-foreground">{isAdmin ? t.auth.roleAdmin : t.auth.roleStaff}</span>
            </Link>
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
        <TwoFactorBanner />
        <PlaceholderBanner />
        <main className="flex-1 p-3 sm:p-6">
          <Outlet />
        </main>
      </div>
    </div>
  );
}
