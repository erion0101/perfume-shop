import { Card, CardContent } from "@perfumeshop/ui";
import { Link } from "react-router";
import { PageHeader } from "@/components/PageHeader";
import { useFragellaUsage } from "@/features/catalog/api";
import { useStock } from "@/features/inventory/api";
import { t } from "@/i18n";
import { useAuth } from "@/lib/auth";

export function DashboardPage() {
  const { isAdmin } = useAuth();
  const usage = useFragellaUsage(isAdmin);
  const low = useStock({ lowStock: true, outOfStock: false, sort: "available", page: 1, pageSize: 1 });

  return (
    <>
      <PageHeader title={t.dashboard.title} description={t.dashboard.intro} />
      <div className="grid gap-4 sm:grid-cols-2">
        <Card>
          <CardContent>
            <Link to="/inventory?low=1" className="grid gap-1">
              <span className="text-sm text-muted-foreground">{t.dashboard.lowStock}</span>
              <span className="text-3xl font-semibold tabular-nums">{low.data?.totalCount ?? "…"}</span>
            </Link>
          </CardContent>
        </Card>
        {isAdmin && usage.data ? (
          <Card>
            <CardContent className="text-sm">{t.dashboard.fragellaRemaining(usage.data.remaining ?? usage.data.limit - usage.data.used, usage.data.limit)}</CardContent>
          </Card>
        ) : null}
      </div>
    </>
  );
}
