import { Button, Card, CardContent, CardHeader, CardTitle, Checkbox, ErrorState, LoadingState, TextareaField, TextField, toast } from "@perfumeshop/ui";
import { ArrowLeft } from "lucide-react";
import { useLayoutEffect, useRef, useState } from "react";
import { Link, useParams } from "react-router";
import { PageHeader } from "@/components/PageHeader";
import { templateName, usePreview, useSaveTemplate, useTemplate, type Template } from "@/features/notifications/api";
import { label, t } from "@/i18n";
import { useAuth } from "@/lib/auth";
import { describeError } from "@/lib/problem";
import { useDebounced } from "@/lib/use-debounced";
import { SegmentBadge } from "./TemplatesPage";

function Editor({ template, canEdit }: { template: Template; canEdit: boolean }) {
  const save = useSaveTemplate(template.key);
  const isSms = template.channel === "Sms";
  const [subject, setSubject] = useState(template.subject ?? "");
  const [body, setBody] = useState(template.body);
  const [transliterate, setTransliterate] = useState(template.transliterate);
  const [enabled, setEnabled] = useState(template.isEnabled);
  const textarea = useRef<HTMLTextAreaElement>(null);
  /** Caret position to restore once React has written an inserted placeholder into the textarea. */
  const caret = useRef<number | null>(null);
  useLayoutEffect(() => {
    if (caret.current !== null && textarea.current) {
      textarea.current.focus();
      textarea.current.setSelectionRange(caret.current, caret.current);
      caret.current = null;
    }
  }, [body]);
  const draft = useDebounced({ subject: isSms ? null : subject, body, transliterate }, 400);
  const preview = usePreview(template.key, draft);

  function insert(name: string) {
    const element = textarea.current;
    const token = `{{${name}}}`;
    if (!element) {
      setBody(body + token);
      return;
    }

    const start = element.selectionStart ?? body.length;
    const end = element.selectionEnd ?? body.length;
    caret.current = start + token.length;
    setBody(body.slice(0, start) + token + body.slice(end));
  }

  async function onSave() {
    try {
      await save.mutateAsync({ subject: isSms ? null : subject, body, transliterate, isEnabled: enabled });
      toast.success(t.notifications.saved);
    } catch (error) {
      toast.error(describeError(error));
    }
  }

  const sms = preview.data?.sms;
  return (
    <div className="grid gap-4 lg:grid-cols-2">
      <Card>
        <CardHeader>
          <CardTitle>{t.notifications.body}</CardTitle>
        </CardHeader>
        <CardContent className="grid gap-4">
          {!canEdit ? <p className="rounded-md bg-muted p-2 text-sm">{t.notifications.readOnly}</p> : null}
          {!isSms ? <TextField label={t.notifications.subject} value={subject} onChange={(e) => setSubject(e.target.value)} disabled={!canEdit} required /> : null}
          <TextareaField ref={textarea} label={t.notifications.body} rows={isSms ? 5 : 10} value={body} onChange={(e) => setBody(e.target.value)} disabled={!canEdit} required />
          <div className="grid gap-1.5">
            <span className="text-sm font-medium">{t.notifications.placeholders}</span>
            <span className="text-xs text-muted-foreground">{t.notifications.placeholdersHint}</span>
            <ul className="flex flex-wrap gap-1.5" aria-label={t.notifications.placeholders}>
              {template.placeholders.map((p) => (
                <li key={p.name}>
                  <Button variant="outline" size="sm" disabled={!canEdit} title={p.sample} onClick={() => insert(p.name)}>
                    {`{{${p.name}}}`}
                  </Button>
                </li>
              ))}
            </ul>
          </div>
          {isSms ? (
            <label className="flex items-center gap-2 text-sm">
              <Checkbox checked={transliterate} onChange={(e) => setTransliterate(e.target.checked)} disabled={!canEdit} /> {t.notifications.transliterate}
            </label>
          ) : null}
          <label className="flex items-center gap-2 text-sm">
            <Checkbox checked={enabled} onChange={(e) => setEnabled(e.target.checked)} disabled={!canEdit} /> {t.notifications.enabled}
          </label>
          {canEdit ? (
            <div>
              <Button disabled={save.isPending || preview.isError} onClick={() => void onSave()}>
                {t.common.save}
              </Button>
            </div>
          ) : null}
        </CardContent>
      </Card>
      <Card>
        <CardHeader>
          <CardTitle>{t.notifications.preview}</CardTitle>
          {sms ? <SegmentBadge segments={sms.segments} encoding={sms.encoding} /> : null}
        </CardHeader>
        <CardContent className="grid gap-3" aria-live="polite">
          {preview.isError ? (
            <p role="alert" className="rounded-md bg-red-50 p-3 text-sm text-destructive">
              {describeError(preview.error)}
            </p>
          ) : preview.data ? (
            <>
              {preview.data.subject ? <p className="font-medium">{preview.data.subject}</p> : null}
              <p className={`whitespace-pre-wrap rounded-md p-3 text-sm ${isSms ? "bg-emerald-50" : "bg-muted"}`} data-testid="template-preview">
                {preview.data.text}
              </p>
              {sms ? (
                <p className="text-xs text-muted-foreground">
                  {t.notifications.encoding(sms.encoding, sms.length, sms.perSegment)}
                  {sms.nonGsmCharacters ? ` · ${sms.nonGsmCharacters}` : ""}
                </p>
              ) : null}
              {sms?.warning ? <p className="rounded-md bg-amber-50 p-2 text-xs text-amber-900">{t.notifications.multiSegment}</p> : null}
            </>
          ) : (
            <LoadingState />
          )}
        </CardContent>
      </Card>
    </div>
  );
}

export function TemplateEditPage() {
  const { key = "" } = useParams();
  const { isAdmin } = useAuth();
  const template = useTemplate(key);

  if (template.isLoading) {
    return <LoadingState />;
  }

  if (template.error || !template.data) {
    return <ErrorState message={describeError(template.error)} onRetry={() => void template.refetch()} />;
  }

  return (
    <>
      <PageHeader
        back={
          <Link to="/notifications/templates" className="inline-flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground">
            <ArrowLeft className="size-4" /> {t.notifications.back}
          </Link>
        }
        title={templateName(template.data)}
        description={`${template.data.key} · ${label(t.notifications.channels, template.data.channel)} · ${label(t.notifications.audiences, template.data.audience)}`}
      />
      <Editor key={template.data.updatedAtUtc} template={template.data} canEdit={isAdmin} />
    </>
  );
}
