import { Card, CardContent, ErrorState, LoadingState, StatTile } from "@perfumeshop/ui";
import { PageHeader } from "@/components/PageHeader";
import { useFragellaUsage } from "@/features/catalog/api";
import { t } from "@/i18n";
import { describeError } from "@/lib/problem";

export function FragellaUsagePage() {
  const usage = useFragellaUsage();
  const u = usage.data;
  const remaining = u ? (u.remaining ?? Math.max(u.limit - u.used, 0)) : 0;
  const percent = u && u.limit > 0 ? Math.min(100, Math.round((u.used / u.limit) * 100)) : 0;

  return (
    <>
      <PageHeader title={t.settings.fragella} description={t.settings.fragellaIntro} />
      {usage.isLoading ? (
        <LoadingState />
      ) : u ? (
        <Card>
          <CardContent className="grid gap-4">
            <p className="text-sm text-muted-foreground">
              {t.settings.month}: {u.month.slice(0, 7).split("-").reverse().join(".")}
            </p>
            <div className="grid gap-3 sm:grid-cols-3">
              <StatTile label={t.settings.used} value={u.used} />
              <StatTile label={t.settings.remaining} value={remaining} tone={remaining === 0 ? "danger" : percent >= 80 ? "warning" : "neutral"} />
              <StatTile label={t.settings.limit} value={u.limit} />
            </div>
            <div
              role="meter"
              aria-label={t.settings.used}
              aria-valuemin={0}
              aria-valuemax={u.limit}
              aria-valuenow={u.used}
              aria-valuetext={`${u.used} / ${u.limit}`}
              className="h-2 overflow-hidden rounded-full bg-muted"
            >
              <div className={`h-full rounded-full ${percent >= 80 ? "bg-amber-600" : "bg-chart-1"}`} style={{ width: `${percent}%` }} />
            </div>
            <p className="text-xs text-muted-foreground">{t.settings.fragellaHint}</p>
          </CardContent>
        </Card>
      ) : (
        <ErrorState message={describeError(usage.error)} onRetry={() => void usage.refetch()} />
      )}
    </>
  );
}
