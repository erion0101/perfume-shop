import { Badge, Button, DataTable, Dialog, Select, StatusBadge, TextField, toast } from "@perfumeshop/ui";
import { RotateCcw } from "lucide-react";
import { useState } from "react";
import { PageHeader } from "@/components/PageHeader";
import { SearchInput } from "@/components/SearchInput";
import { templateName, useMessages, useResend, useTemplates, type Message } from "@/features/notifications/api";
import { formatDateTime, label, t } from "@/i18n";
import { describeError } from "@/lib/problem";
import { useSearchState } from "@/lib/use-search-state";
import { SegmentBadge } from "./TemplatesPage";

const defaults = { status: "", channel: "", templateKey: "", recipient: "", orderNumber: "", from: "", to: "", page: "1" };
const pageSize = 50;

export const messageBadges = {
  Queued: { label: t.notifications.statuses.Queued!, tone: "info" as const },
  Sent: { label: t.notifications.statuses.Sent!, tone: "success" as const },
  Failed: { label: t.notifications.statuses.Failed!, tone: "danger" as const },
};

export function ResendButton({ message }: { message: Message }) {
  const resend = useResend();
  if (message.status !== "Failed") {
    return null;
  }

  return (
    <Button
      variant="outline"
      size="sm"
      disabled={resend.isPending}
      onClick={(e) => {
        e.stopPropagation();
        void resend.mutateAsync(message.id).then(
          () => toast.success(t.notifications.resent),
          (error: unknown) => toast.error(describeError(error)),
        );
      }}
    >
      <RotateCcw /> {t.notifications.resend}
    </Button>
  );
}

function MessageDialog({ message, onClose }: { message: Message; onClose: () => void }) {
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()} wide title={`${t.notifications.message} · ${message.templateKey}`} footer={<ResendButton message={message} />}>
      <div className="grid gap-3 text-sm">
        <p className="flex flex-wrap items-center gap-2">
          <StatusBadge status={message.status} map={messageBadges} />
          {label(t.notifications.channels, message.channel)} → <strong>{message.recipient}</strong>
          {message.segments ? <SegmentBadge segments={message.segments} encoding={message.encoding ?? undefined} /> : null}
          {message.heldForQuietHours ? <Badge tone="info">{t.notifications.heldQuiet}</Badge> : null}
        </p>
        {message.subject ? <p className="font-medium">{message.subject}</p> : null}
        <p className="whitespace-pre-wrap rounded-md bg-muted p-3">{message.body}</p>
        <p className="text-muted-foreground">
          {t.notifications.created}: {formatDateTime(message.createdAtUtc)}
          {message.sentAtUtc ? ` · ${t.notifications.sent}: ${formatDateTime(message.sentAtUtc)}` : ""}
          {message.status === "Queued" ? ` · ${t.notifications.nextAttempt}: ${formatDateTime(message.nextAttemptAtUtc)}` : ""}
        </p>
        {message.lastError ? <p className="text-destructive">{t.notifications.error}: {message.lastError}</p> : null}
        <p className="font-medium">{t.notifications.attempts}</p>
        <ol className="grid gap-2">
          {message.attemptLog.map((a, i) => (
            <li key={i} className="rounded-md border border-border p-2">
              <p className="flex flex-wrap justify-between gap-2">
                <span>{formatDateTime(a.attemptedAtUtc)}</span>
                <Badge tone={a.succeeded ? "success" : "danger"}>{a.succeeded ? t.notifications.statuses.Sent : t.notifications.statuses.Failed}</Badge>
              </p>
              <p className="text-xs text-muted-foreground">
                {t.notifications.provider}: {a.provider}
                {a.providerMessageId ? ` · ${a.providerMessageId}` : ""}
              </p>
              {a.error ? <p className="text-xs text-destructive">{a.error}</p> : null}
              {a.response ? (
                <pre className="mt-1 overflow-x-auto whitespace-pre-wrap rounded bg-muted p-1.5 text-xs" aria-label={t.notifications.response}>
                  {a.response}
                </pre>
              ) : null}
            </li>
          ))}
        </ol>
      </div>
    </Dialog>
  );
}

export function MessagesPage() {
  const [state, set] = useSearchState(defaults);
  const page = Number(state.page) || 1;
  const messages = useMessages({ ...state, page, pageSize });
  const templates = useTemplates();
  const [open, setOpen] = useState<Message | null>(null);

  return (
    <>
      <PageHeader title={t.notifications.log} />
      <DataTable<Message>
        caption={t.notifications.log}
        toolbar={
          <>
            <SearchInput value={state.recipient} onChange={(recipient) => set({ recipient })} placeholder={t.notifications.recipient} className="sm:max-w-48" />
            <SearchInput value={state.orderNumber} onChange={(orderNumber) => set({ orderNumber })} placeholder={t.notifications.orderNumber} className="sm:max-w-44" />
            <Select className="w-full sm:w-40" aria-label={t.notifications.status} value={state.status} onChange={(e) => set({ status: e.target.value })}>
              <option value="">{t.notifications.allStatuses}</option>
              {Object.entries(t.notifications.statuses).map(([v, text]) => (
                <option key={v} value={v}>
                  {text}
                </option>
              ))}
            </Select>
            <Select className="w-full sm:w-36" aria-label={t.notifications.channel} value={state.channel} onChange={(e) => set({ channel: e.target.value })}>
              <option value="">{t.notifications.allChannels}</option>
              <option value="Sms">SMS</option>
              <option value="Email">Email</option>
            </Select>
            <Select className="w-full sm:w-56" aria-label={t.notifications.key} value={state.templateKey} onChange={(e) => set({ templateKey: e.target.value })}>
              <option value="">{t.notifications.allTemplates}</option>
              {(templates.data ?? []).map((tpl) => (
                <option key={tpl.key} value={tpl.key}>
                  {templateName(tpl)}
                </option>
              ))}
            </Select>
            <TextField label={t.orders.from} type="date" value={state.from} onChange={(e) => set({ from: e.target.value })} className="w-full sm:w-40" />
            <TextField label={t.orders.to} type="date" value={state.to} onChange={(e) => set({ to: e.target.value })} className="w-full sm:w-40" />
          </>
        }
        columns={[
          { key: "created", header: t.notifications.created, cell: (m) => <span className="whitespace-nowrap tabular-nums">{formatDateTime(m.createdAtUtc)}</span> },
          { key: "status", header: t.notifications.status, cell: (m) => <StatusBadge status={m.status} map={messageBadges} /> },
          { key: "recipient", header: t.notifications.recipient, cell: (m) => `${label(t.notifications.channels, m.channel)} · ${m.recipient}` },
          { key: "template", header: t.notifications.key, cell: (m) => <code className="text-xs">{m.templateKey}</code>, hideOnMobile: true },
          { key: "order", header: t.notifications.orderNumber, cell: (m) => m.orderNumber ?? t.common.none, hideOnMobile: true },
          { key: "attempts", header: t.notifications.attempts, cell: (m) => m.attemptLog.length, className: "text-right", hideOnMobile: true },
          { key: "resend", header: t.common.actions, cell: (m) => <ResendButton message={m} /> },
        ]}
        rows={messages.data?.items}
        rowKey={(m) => m.id}
        total={messages.data?.totalCount ?? 0}
        page={page}
        pageSize={pageSize}
        onPageChange={(next) => set({ page: String(next) })}
        isLoading={messages.isLoading}
        error={messages.error ? describeError(messages.error) : null}
        onRetry={() => void messages.refetch()}
        emptyTitle={t.notifications.empty}
        onRowClick={setOpen}
      />
      {open ? <MessageDialog message={open} onClose={() => setOpen(null)} /> : null}
    </>
  );
}
