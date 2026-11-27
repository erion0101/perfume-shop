import { zodResolver } from "@hookform/resolvers/zod";
import { Button, Card, CardContent, CardHeader, CardTitle, Checkbox, SelectField, TextareaField, TextField, toast } from "@perfumeshop/ui";
import { useForm, type UseFormRegisterReturn } from "react-hook-form";
import { z } from "zod";
import { toDetailsInput, useUpdateFragrance, type Fragrance, type FragranceDetailsInput } from "@/features/catalog/api";
import { label, options, t } from "@/i18n";
import { applyFieldErrors, describeError } from "@/lib/problem";

const nextYear = new Date().getFullYear() + 1;

const schema = z.object({
  name: z.string().trim().min(1, t.fragrance.nameRequired).max(200, t.fragrance.nameRequired),
  slug: z.string().trim().max(200),
  description: z.string().max(8000),
  concentration: z.string().min(1),
  gender: z.string().min(1),
  olfactiveFamily: z.string(),
  moodOverride: z.string(),
  longevity: z.string(),
  sillage: z.string(),
  launchYear: z.string().refine((v) => v === "" || (/^\d{4}$/.test(v) && Number(v) >= 1900 && Number(v) <= nextYear), t.fragrance.yearInvalid),
  perfumers: z.string().max(500),
  seasons: z.array(z.string()),
  occasions: z.array(z.string()),
});

type DetailsForm = z.infer<typeof schema>;

function CheckboxGroup({ legend, values, register }: { legend: string; values: Record<string, string>; register: UseFormRegisterReturn }) {
  return (
    <fieldset className="grid gap-2">
      <legend className="mb-1 text-sm font-medium">{legend}</legend>
      <div className="flex flex-wrap gap-x-4 gap-y-2">
        {Object.entries(values).map(([value, text]) => (
          <label key={value} className="flex items-center gap-2 text-sm">
            <Checkbox value={value} {...register} />
            {text}
          </label>
        ))}
      </div>
    </fieldset>
  );
}

export function DetailsCard({ fragrance }: { fragrance: Fragrance }) {
  const update = useUpdateFragrance(fragrance.id);
  const { register, handleSubmit, formState, setError } = useForm<DetailsForm>({
    resolver: zodResolver(schema),
    values: {
      name: fragrance.name,
      slug: fragrance.slug,
      description: fragrance.description ?? "",
      concentration: fragrance.concentration,
      gender: fragrance.gender,
      olfactiveFamily: fragrance.olfactiveFamily ?? "",
      moodOverride: fragrance.moodOverride ?? "",
      longevity: fragrance.longevity ?? "",
      sillage: fragrance.sillage ?? "",
      launchYear: fragrance.launchYear ? String(fragrance.launchYear) : "",
      perfumers: fragrance.perfumers.join(", "),
      seasons: fragrance.seasons,
      occasions: fragrance.occasions,
    },
  });

  const onSubmit = handleSubmit(async (v) => {
    const body: FragranceDetailsInput = {
      ...toDetailsInput(fragrance),
      name: v.name,
      slug: v.slug || null,
      description: v.description.trim() || null,
      concentration: v.concentration as FragranceDetailsInput["concentration"],
      gender: v.gender as FragranceDetailsInput["gender"],
      olfactiveFamily: (v.olfactiveFamily || null) as FragranceDetailsInput["olfactiveFamily"],
      moodOverride: (v.moodOverride || null) as FragranceDetailsInput["moodOverride"],
      longevity: (v.longevity || null) as FragranceDetailsInput["longevity"],
      sillage: (v.sillage || null) as FragranceDetailsInput["sillage"],
      launchYear: v.launchYear ? Number(v.launchYear) : null,
      perfumers: v.perfumers.split(",").map((p) => p.trim()).filter(Boolean),
      seasons: v.seasons as FragranceDetailsInput["seasons"],
      occasions: v.occasions as FragranceDetailsInput["occasions"],
    };
    try {
      await update.mutateAsync(body);
      toast.success(t.common.saved);
    } catch (error) {
      applyFieldErrors(error, setError, Object.keys(schema.shape));
      toast.error(describeError(error));
    }
  });

  return (
    <Card>
      <CardHeader>
        <CardTitle>{t.fragrance.details}</CardTitle>
      </CardHeader>
      <CardContent>
        <form className="grid gap-4" onSubmit={(e) => void onSubmit(e)} noValidate aria-label={t.fragrance.details}>
          <div className="grid gap-4 sm:grid-cols-2">
            <TextField label={t.fragrance.name} required error={formState.errors.name?.message} {...register("name")} />
            <TextField label={t.fragrance.slug} error={formState.errors.slug?.message} {...register("slug")} />
          </div>
          <TextareaField label={t.fragrance.description} hint={t.fragrance.descriptionHint} rows={6} error={formState.errors.description?.message} {...register("description")} />
          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
            <SelectField label={t.fragrance.concentration} required options={options(t.enums.concentration)} {...register("concentration")} />
            <SelectField label={t.fragrance.gender} required options={options(t.enums.gender)} {...register("gender")} />
            <SelectField label={t.fragrance.family} placeholder={t.common.none} options={options(t.enums.family)} {...register("olfactiveFamily")} />
            <SelectField
              label={t.fragrance.mood}
              hint={t.fragrance.moodHint}
              placeholder={t.fragrance.moodAuto(fragrance.moodOverride || !fragrance.mood ? null : label(t.enums.mood, fragrance.mood))}
              options={options(t.enums.mood)}
              {...register("moodOverride")}
            />
            <SelectField label={t.fragrance.longevity} placeholder={t.common.none} options={options(t.enums.longevity)} {...register("longevity")} />
            <SelectField label={t.fragrance.sillage} placeholder={t.common.none} options={options(t.enums.sillage)} {...register("sillage")} />
            <TextField label={t.fragrance.launchYear} inputMode="numeric" error={formState.errors.launchYear?.message} {...register("launchYear")} />
          </div>
          <TextField label={t.fragrance.perfumers} hint={t.fragrance.perfumersHint} {...register("perfumers")} />
          <div className="grid gap-4 sm:grid-cols-2">
            <CheckboxGroup legend={t.fragrance.seasons} values={t.enums.season} register={register("seasons")} />
            <CheckboxGroup legend={t.fragrance.occasions} values={t.enums.occasion} register={register("occasions")} />
          </div>
          <div className="flex justify-end">
            <Button type="submit" disabled={formState.isSubmitting || !formState.isDirty}>
              {formState.isSubmitting ? t.common.saving : t.common.save}
            </Button>
          </div>
        </form>
      </CardContent>
    </Card>
  );
}
