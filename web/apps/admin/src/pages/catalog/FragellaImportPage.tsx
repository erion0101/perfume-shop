import { Badge, Button, Card, CardContent, EmptyState, ErrorState, LoadingState, SelectField, TextField, toast } from "@perfumeshop/ui";
import { Search } from "lucide-react";
import { useState } from "react";
import { Link, useNavigate } from "react-router";
import { PageHeader } from "@/components/PageHeader";
import { useFragellaSearch, useFragellaUsage, useImportFragrance, type FragellaItem, type ImportInput } from "@/features/catalog/api";
import { options, t } from "@/i18n";
import { ApiError, describeError } from "@/lib/problem";

/** Fragella sometimes leaves concentration or gender out: then staff choose them and import again. */
const needsChoice = new Set(["Catalog.Fragella.ConcentrationUnknown", "Catalog.Fragella.GenderUnknown"]);

function ResultCard({ item }: { item: FragellaItem }) {
  const navigate = useNavigate();
  const importFragrance = useImportFragrance();
  const [missing, setMissing] = useState<string | null>(null);
  const [concentration, setConcentration] = useState("");
  const [gender, setGender] = useState("");

  async function runImport() {
    try {
      const fragrance = await importFragrance.mutateAsync({
        fragellaId: item.fragellaId,
        concentration: (concentration || null) as ImportInput["concentration"],
        gender: (gender || null) as ImportInput["gender"],
        brandId: null,
      });
      toast.success(t.fragella.imported);
      navigate(`/catalog/fragrances/${fragrance.id}`);
    } catch (error) {
      if (error instanceof ApiError && error.code && needsChoice.has(error.code)) {
        setMissing(error.code);
      }

      toast.error(describeError(error));
    }
  }

  return (
    <li>
      <Card className="flex h-full flex-col">
        <CardContent className="flex flex-1 gap-3">
          {item.imageUrl ? (
            <img src={item.imageUrl} alt="" className="size-20 shrink-0 rounded-md object-contain" loading="lazy" referrerPolicy="no-referrer" />
          ) : (
            <div className="size-20 shrink-0 rounded-md bg-muted" aria-hidden />
          )}
          <div className="grid min-w-0 flex-1 content-start gap-1 text-sm">
            <p className="font-semibold">{item.name}</p>
            <p className="text-muted-foreground">{item.brand}</p>
            <p className="text-xs text-muted-foreground">
              {[item.year && `${t.fragella.year} ${item.year}`, item.gender, item.oilType].filter(Boolean).join(" · ")}
            </p>
            {missing ? (
              <div className="mt-2 grid gap-2">
                {missing === "Catalog.Fragella.ConcentrationUnknown" || concentration ? (
                  <SelectField label={t.fragrance.concentration} placeholder={t.common.choose} options={options(t.enums.concentration)} value={concentration} onChange={(e) => setConcentration(e.target.value)} />
                ) : null}
                {missing === "Catalog.Fragella.GenderUnknown" || gender ? (
                  <SelectField label={t.fragrance.gender} placeholder={t.common.choose} options={options(t.enums.gender)} value={gender} onChange={(e) => setGender(e.target.value)} />
                ) : null}
              </div>
            ) : null}
          </div>
        </CardContent>
        <div className="flex items-center justify-end gap-2 border-t border-border px-4 py-3">
          {item.importedFragranceId ? (
            <>
              <Badge tone="success">{t.fragella.alreadyImported}</Badge>
              <Button asChild size="sm" variant="outline">
                <Link to={`/catalog/fragrances/${item.importedFragranceId}`}>{t.fragella.openImported}</Link>
              </Button>
            </>
          ) : (
            <Button size="sm" disabled={importFragrance.isPending} onClick={() => void runImport()}>
              {importFragrance.isPending ? t.fragella.importing : t.fragella.import}
            </Button>
          )}
        </div>
      </Card>
    </li>
  );
}

export function FragellaImportPage() {
  const [text, setText] = useState("");
  const [query, setQuery] = useState("");
  const usage = useFragellaUsage();
  const search = useFragellaSearch(query);
  const tooShort = text.trim().length > 0 && text.trim().length < 2;

  return (
    <>
      <PageHeader
        title={t.fragella.title}
        description={t.fragella.intro}
        actions={
          usage.data ? (
            <Badge tone={(usage.data.remaining ?? 0) < 100 ? "warning" : "brand"} data-testid="fragella-remaining">
              {t.fragella.remaining(usage.data.remaining ?? usage.data.limit - usage.data.used, usage.data.limit)}
            </Badge>
          ) : null
        }
      />
      <form
        className="mb-4 flex flex-col gap-3 sm:flex-row sm:items-end"
        onSubmit={(e) => {
          e.preventDefault();
          if (text.trim().length >= 2) {
            setQuery(text.trim());
          }
        }}
      >
        <TextField
          className="flex-1"
          label={t.fragella.searchLabel}
          placeholder={t.fragella.searchPlaceholder}
          value={text}
          onChange={(e) => setText(e.target.value)}
          error={tooShort ? t.fragella.minChars : undefined}
        />
        <Button type="submit" disabled={text.trim().length < 2 || search.isFetching}>
          <Search /> {t.fragella.searchButton}
        </Button>
      </form>

      {!query ? (
        <EmptyState title={t.fragella.startHint} />
      ) : search.isLoading ? (
        <LoadingState />
      ) : search.error ? (
        <ErrorState message={describeError(search.error)} onRetry={() => void search.refetch()} />
      ) : search.data && search.data.items.length === 0 ? (
        <EmptyState title={t.fragella.noResults} />
      ) : search.data ? (
        <>
          {search.data.fromCache ? <p className="mb-2 text-xs text-muted-foreground">{t.fragella.fromCache}</p> : null}
          <ul className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3" aria-label={t.fragella.title}>
            {search.data.items.map((item) => (
              <ResultCard key={item.fragellaId} item={item} />
            ))}
          </ul>
        </>
      ) : null}
    </>
  );
}
