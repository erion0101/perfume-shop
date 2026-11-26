import { zodResolver } from "@hookform/resolvers/zod";
import { Badge, Button, Card, CardContent, CardHeader, CardTitle, Input, TextareaField, toast } from "@perfumeshop/ui";
import { Plus, Trash2 } from "lucide-react";
import { useState } from "react";
import { useFieldArray, useForm } from "react-hook-form";
import { z } from "zod";
import { toDetailsInput, useUpdateFragrance, type Fragrance } from "@/features/catalog/api";
import { t } from "@/i18n";
import { describeError } from "@/lib/problem";

const schema = z.object({
  top: z.string(),
  heart: z.string(),
  base: z.string(),
  accords: z.array(
    z.object({
      name: z.string().trim().min(1, t.fragrance.accordName),
      percentage: z.string().regex(/^(100|[1-9]?\d)$/, t.fragrance.percentInvalid),
    }),
  ),
});

type NotesForm = z.infer<typeof schema>;

const toText = (notes: { name: string }[]) => notes.map((n) => n.name).join("\n");
const toNotes = (text: string, existing: { name: string; imageUrl: string | null }[]) =>
  text
    .split(/[\n,]/)
    .map((name) => name.trim())
    .filter(Boolean)
    .map((name) => ({ name, imageUrl: existing.find((n) => n.name.toLowerCase() === name.toLowerCase())?.imageUrl ?? null }));

function Tier({ title, notes }: { title: string; notes: { name: string }[] }) {
  return (
    <div className="grid gap-2">
      <p className="text-sm font-medium">{title}</p>
      {notes.length === 0 ? (
        <p className="text-sm text-muted-foreground">{t.fragrance.noNotes}</p>
      ) : (
        <ul className="flex flex-wrap gap-1.5">
          {notes.map((n) => (
            <li key={n.name}>
              <Badge tone="brand">{n.name}</Badge>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

/** Note pyramid and main accords from Fragella: shown, and editable by hand. */
export function NotesCard({ fragrance }: { fragrance: Fragrance }) {
  const [editing, setEditing] = useState(false);
  const update = useUpdateFragrance(fragrance.id);
  const { notes, accords } = fragrance;
  const { register, control, handleSubmit, formState, reset } = useForm<NotesForm>({
    resolver: zodResolver(schema),
    values: {
      top: toText(notes.top),
      heart: toText(notes.heart),
      base: toText(notes.base),
      accords: accords.map((a) => ({ name: a.name, percentage: String(a.percentage) })),
    },
  });
  const accordRows = useFieldArray({ control, name: "accords" });

  const onSubmit = handleSubmit(async (v) => {
    const all = [...notes.top, ...notes.heart, ...notes.base];
    try {
      await update.mutateAsync({
        ...toDetailsInput(fragrance),
        notes: { top: toNotes(v.top, all), heart: toNotes(v.heart, all), base: toNotes(v.base, all) },
        accords: v.accords.map((a) => ({ name: a.name.trim(), percentage: Number(a.percentage) })),
      });
      toast.success(t.common.saved);
      setEditing(false);
    } catch (error) {
      toast.error(describeError(error));
    }
  });

  return (
    <Card>
      <CardHeader>
        <CardTitle>{t.fragrance.notesAndAccords}</CardTitle>
        <Button
          variant="outline"
          size="sm"
          onClick={() => {
            reset();
            setEditing(!editing);
          }}
        >
          {editing ? t.common.cancel : t.fragrance.editNotes}
        </Button>
      </CardHeader>
      <CardContent>
        {!editing ? (
          <div className="grid gap-6 lg:grid-cols-2">
            <div className="grid gap-4" aria-label={t.fragrance.pyramid}>
              <Tier title={t.fragrance.top} notes={notes.top} />
              <Tier title={t.fragrance.heart} notes={notes.heart} />
              <Tier title={t.fragrance.base} notes={notes.base} />
            </div>
            <div className="grid content-start gap-2">
              <p className="text-sm font-medium">{t.fragrance.accords}</p>
              {accords.length === 0 ? (
                <p className="text-sm text-muted-foreground">{t.fragrance.noAccords}</p>
              ) : (
                <ul className="grid gap-1.5">
                  {[...accords]
                    .sort((a, b) => b.percentage - a.percentage)
                    .map((a) => (
                      <li key={a.name} className="grid grid-cols-[8rem_1fr_3rem] items-center gap-2 text-sm">
                        <span className="truncate">{a.name}</span>
                        <span className="h-2 rounded-full bg-muted" aria-hidden>
                          <span className="block h-2 rounded-full bg-brand-500" style={{ width: `${a.percentage}%` }} />
                        </span>
                        <span className="text-right tabular-nums text-muted-foreground">{a.percentage}%</span>
                      </li>
                    ))}
                </ul>
              )}
            </div>
          </div>
        ) : (
          <form className="grid gap-4" onSubmit={(e) => void onSubmit(e)} noValidate aria-label={t.fragrance.notesAndAccords}>
            <div className="grid gap-4 md:grid-cols-3">
              <TextareaField label={t.fragrance.top} hint={t.fragrance.notesHint} rows={5} {...register("top")} />
              <TextareaField label={t.fragrance.heart} hint={t.fragrance.notesHint} rows={5} {...register("heart")} />
              <TextareaField label={t.fragrance.base} hint={t.fragrance.notesHint} rows={5} {...register("base")} />
            </div>
            <fieldset className="grid gap-2">
              <legend className="mb-1 text-sm font-medium">{t.fragrance.accords}</legend>
              {accordRows.fields.map((field, index) => (
                <div key={field.id} className="flex items-start gap-2">
                  <Input aria-label={`${t.fragrance.accordName} ${index + 1}`} aria-invalid={Boolean(formState.errors.accords?.[index]?.name) || undefined} {...register(`accords.${index}.name`)} />
                  <Input
                    className="w-20"
                    inputMode="numeric"
                    aria-label={`${t.fragrance.accordPercent} ${index + 1}`}
                    aria-invalid={Boolean(formState.errors.accords?.[index]?.percentage) || undefined}
                    {...register(`accords.${index}.percentage`)}
                  />
                  <Button variant="ghost" size="icon" aria-label={`${t.common.remove} ${index + 1}`} onClick={() => accordRows.remove(index)}>
                    <Trash2 />
                  </Button>
                </div>
              ))}
              {formState.errors.accords ? <p className="text-xs text-destructive">{t.fragrance.percentInvalid}</p> : null}
              <div>
                <Button variant="outline" size="sm" onClick={() => accordRows.append({ name: "", percentage: "50" })}>
                  <Plus /> {t.fragrance.addAccord}
                </Button>
              </div>
            </fieldset>
            <div className="flex justify-end">
              <Button type="submit" disabled={formState.isSubmitting}>
                {formState.isSubmitting ? t.common.saving : t.common.save}
              </Button>
            </div>
          </form>
        )}
      </CardContent>
    </Card>
  );
}
