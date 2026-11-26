import { Button, ConfirmDialog, ErrorState, LoadingState, StatusBadge, toast } from "@perfumeshop/ui";
import { ArrowLeft } from "lucide-react";
import { Link, useParams } from "react-router";
import { PageHeader } from "@/components/PageHeader";
import { usePublish, useFragrance } from "@/features/catalog/api";
import { formatDate, label, t } from "@/i18n";
import { describeError } from "@/lib/problem";
import { fragranceStatusBadges } from "./FragrancesPage";
import { DetailsCard } from "./fragrance/DetailsCard";
import { NotesCard } from "./fragrance/NotesCard";
import { VariantsCard } from "./fragrance/VariantsCard";

export function FragranceEditPage() {
  const { id = "" } = useParams();
  const fragrance = useFragrance(id);
  const publish = usePublish(id);

  if (fragrance.isLoading) {
    return <LoadingState />;
  }

  if (fragrance.error || !fragrance.data) {
    return <ErrorState message={describeError(fragrance.error)} onRetry={() => void fragrance.refetch()} />;
  }

  const f = fragrance.data;
  const published = f.status === "Published";

  async function changePublished() {
    try {
      await publish.mutateAsync(!published);
      toast.success(published ? t.fragrance.unpublished : t.fragrance.published);
    } catch (error) {
      toast.error(describeError(error)); // e.g. "needs an active variant with a price"
      throw error;
    }
  }

  return (
    <>
      <PageHeader
        back={
          <Link to="/catalog/fragrances" className="inline-flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground">
            <ArrowLeft className="size-4" /> {t.fragrance.backToList}
          </Link>
        }
        title={`${f.brand.name} ${f.name}`}
        description={
          <span className="flex flex-wrap items-center gap-2">
            <StatusBadge status={f.status as "Draft"} map={fragranceStatusBadges} />
            <span>{label(t.enums.concentration, f.concentration)}</span>
            {f.fragellaImportedAtUtc ? <span>· {t.fragrance.importedFromFragella(formatDate(f.fragellaImportedAtUtc))}</span> : null}
          </span>
        }
        actions={
          <ConfirmDialog
            trigger={<Button variant={published ? "outline" : "default"}>{published ? t.fragrance.unpublish : t.fragrance.publish}</Button>}
            title={published ? t.fragrance.unpublishConfirm : t.fragrance.publishConfirm}
            description={published ? t.fragrance.unpublishHint : t.fragrance.publishHint}
            confirmLabel={published ? t.fragrance.unpublish : t.fragrance.publish}
            destructive={published}
            onConfirm={changePublished}
          />
        }
      />
      <div className="grid gap-4">
        <VariantsCard fragrance={f} />
        <DetailsCard fragrance={f} />
        <NotesCard fragrance={f} />
      </div>
    </>
  );
}
