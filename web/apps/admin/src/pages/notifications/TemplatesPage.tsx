import { Badge, DataTable, type BadgeTone } from "@perfumeshop/ui";
import { AlertTriangle } from "lucide-react";
import { useNavigate } from "react-router";
import { PageHeader } from "@/components/PageHeader";
import { templateName, useTemplates, type Template } from "@/features/notifications/api";
import { label, t } from "@/i18n";
import { describeError } from "@/lib/problem";

/** "1 SMS" / "2 SMS ⚠" (more than one segment costs more). */
export function SegmentBadge({ segments, encoding }: { segments: number; encoding?: string }) {
  const tone: BadgeTone = segments > 1 ? "warning" : "success";
  return (
    <Badge tone={tone} title={encoding}>
      {segments > 1 ? <AlertTriangle className="size-3" aria-hidden /> : null}
      {t.notifications.smsCount(segments)}
      {segments > 1 ? <span className="sr-only"> – {t.notifications.multiSegment}</span> : null}
    </Badge>
  );
}

export function TemplatesPage() {
  const navigate = useNavigate();
  const templates = useTemplates();
  const rows = templates.data ?? [];

  return (
    <>
      <PageHeader title={t.notifications.templates} description={t.notifications.templatesIntro} />
      <DataTable<Template>
        caption={t.notifications.templates}
        columns={[
          {
            key: "key",
            header: t.notifications.key,
            cell: (tpl) => (
              <span className="grid">
                <span className="font-medium">{templateName(tpl)}</span>
                <code className="text-xs text-muted-foreground">{tpl.key}</code>
              </span>
            ),
          },
          { key: "channel", header: t.notifications.channel, cell: (tpl) => `${label(t.notifications.channels, tpl.channel)} · ${label(t.notifications.audiences, tpl.audience)}` },
          {
            key: "segments",
            header: t.notifications.segments,
            cell: (tpl) =>
              tpl.sms ? (
                <span className="flex flex-wrap items-center gap-1 text-xs">
                  <SegmentBadge segments={tpl.sms.segments} encoding={tpl.sms.encoding} /> {t.notifications.withTranslit}
                  {tpl.smsWithoutTransliteration ? (
                    <>
                      {" · "}
                      <SegmentBadge segments={tpl.smsWithoutTransliteration.segments} encoding={tpl.smsWithoutTransliteration.encoding} /> {t.notifications.withoutTranslit}
                    </>
                  ) : null}
                </span>
              ) : (
                t.common.none
              ),
          },
          {
            key: "warning",
            header: "",
            cell: (tpl) => (tpl.sms?.warning ? <span className="text-xs text-amber-800">{t.notifications.multiSegment}</span> : null),
            hideOnMobile: true,
          },
          { key: "enabled", header: t.notifications.enabled, cell: (tpl) => <Badge tone={tpl.isEnabled ? "success" : "neutral"}>{tpl.isEnabled ? t.notifications.enabled : t.notifications.disabled}</Badge> },
        ]}
        rows={templates.data}
        rowKey={(tpl) => tpl.key}
        total={rows.length}
        page={1}
        pageSize={Math.max(rows.length, 1)}
        onPageChange={() => {}}
        isLoading={templates.isLoading}
        error={templates.error ? describeError(templates.error) : null}
        onRetry={() => void templates.refetch()}
        onRowClick={(tpl) => navigate(`/notifications/templates/${tpl.key}`)}
      />
    </>
  );
}
