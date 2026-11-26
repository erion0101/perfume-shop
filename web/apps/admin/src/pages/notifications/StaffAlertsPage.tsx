import { Button, Card, CardContent, Checkbox, ErrorState, LoadingState, TextareaField, toast } from "@perfumeshop/ui";
import { useState } from "react";
import { PageHeader } from "@/components/PageHeader";
import { useAlertSettings, useSaveAlertSettings } from "@/features/notifications/api";
import { t } from "@/i18n";
import { useAuth } from "@/lib/auth";
import { describeError } from "@/lib/problem";

type Kind = "newOrder" | "failedReceipt" | "lowStock" | "unpaidDeliveries" | "jobFailures";
const kinds: Kind[] = ["newOrder", "failedReceipt", "lowStock", "unpaidDeliveries", "jobFailures"];
const email = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

function Form({ initial, canEdit }: { initial: { recipients: string[] } & Record<Kind, boolean>; canEdit: boolean }) {
  const save = useSaveAlertSettings();
  const [recipients, setRecipients] = useState(initial.recipients.join("\n"));
  const [on, setOn] = useState<Record<Kind, boolean>>(Object.fromEntries(kinds.map((k) => [k, initial[k]])) as Record<Kind, boolean>);
  const list = recipients.split(/[\n,;]/).map((r) => r.trim()).filter(Boolean);
  const invalid = list.length > 10 || list.some((r) => !email.test(r));

  async function submit() {
    try {
      await save.mutateAsync({ recipients: list, ...on });
      toast.success(t.notifications.alertsSaved);
    } catch (error) {
      toast.error(describeError(error));
    }
  }

  return (
    <Card>
      <CardContent className="grid gap-4 sm:max-w-xl">
        <TextareaField
          label={t.notifications.recipients}
          hint={list.length === 0 ? t.notifications.noRecipients : t.notifications.recipientsHint}
          rows={4}
          value={recipients}
          onChange={(e) => setRecipients(e.target.value)}
          error={invalid ? t.notifications.invalidRecipients : undefined}
          disabled={!canEdit}
        />
        <fieldset className="grid gap-2">
          {kinds.map((kind) => (
            <label key={kind} className="flex items-center gap-2 text-sm">
              <Checkbox checked={on[kind]} onChange={(e) => setOn({ ...on, [kind]: e.target.checked })} disabled={!canEdit} /> {t.notifications.alertKinds[kind]}
            </label>
          ))}
        </fieldset>
        {canEdit ? (
          <div>
            <Button disabled={invalid || save.isPending} onClick={() => void submit()}>
              {t.common.save}
            </Button>
          </div>
        ) : null}
      </CardContent>
    </Card>
  );
}

export function StaffAlertsPage() {
  const { isAdmin } = useAuth();
  const settings = useAlertSettings();
  return (
    <>
      <PageHeader title={t.notifications.alerts} description={t.notifications.alertsIntro} />
      {settings.isLoading ? (
        <LoadingState />
      ) : settings.data ? (
        <Form key={settings.data.updatedAtUtc} initial={settings.data} canEdit={isAdmin} />
      ) : (
        <ErrorState message={describeError(settings.error)} onRetry={() => void settings.refetch()} />
      )}
    </>
  );
}
