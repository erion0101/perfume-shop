import { Button, Card, CardContent, CardHeader, CardTitle, ConfirmDialog, EmptyState, formatMoney, StatusBadge, toast } from "@perfumeshop/ui";
import { Pencil, Plus } from "lucide-react";
import { useState } from "react";
import { Link } from "react-router";
import { useVariantActive, type Fragrance, type Variant } from "@/features/catalog/api";
import { label, t } from "@/i18n";
import { describeError } from "@/lib/problem";
import { imageSrc, VariantDialog } from "./VariantDialog";

const activeBadges = {
  active: { label: t.variants.active, tone: "success" as const },
  inactive: { label: t.variants.inactive, tone: "neutral" as const },
};

function size(v: Variant) {
  return v.size ? `${String(v.size.value).replace(".", ",")} ${label(t.enums.sizeUnit, v.size.unit)}` : t.common.none;
}

export function VariantsCard({ fragrance }: { fragrance: Fragrance }) {
  const [editing, setEditing] = useState<Variant | null>(null);
  const [open, setOpen] = useState(false);
  const setActive = useVariantActive(fragrance.id);

  async function toggle(variant: Variant) {
    try {
      await setActive.mutateAsync({ variantId: variant.id, active: !variant.isActive });
      toast.success(variant.isActive ? t.variants.deactivated : t.variants.activated);
    } catch (error) {
      toast.error(describeError(error));
      throw error;
    }
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle>{t.fragrance.variants}</CardTitle>
        <Button
          size="sm"
          onClick={() => {
            setEditing(null);
            setOpen(true);
          }}
        >
          <Plus /> {t.variants.add}
        </Button>
      </CardHeader>
      <CardContent>
        {fragrance.variants.length === 0 ? (
          <EmptyState title={t.variants.none} />
        ) : (
          <ul className="grid gap-3" aria-label={t.fragrance.variants}>
            {fragrance.variants.map((v) => (
              <li key={v.id} className="grid gap-3 rounded-md border border-border p-3 sm:grid-cols-[auto_1fr_auto] sm:items-center">
                {v.images[0] ? (
                  <img src={imageSrc(v.images[0].url)} alt={v.images[0].altText ?? ""} className="size-16 rounded-md border border-border object-cover" />
                ) : (
                  <div className="hidden size-16 rounded-md bg-muted sm:block" aria-hidden />
                )}
                <div className="grid gap-1 text-sm">
                  <p className="flex flex-wrap items-center gap-2 font-medium">
                    {label(t.enums.variantType, v.type)} · {size(v)}
                    <StatusBadge status={v.isActive ? "active" : "inactive"} map={activeBadges} />
                  </p>
                  <p className="text-muted-foreground">
                    {t.variants.sku}: {v.sku}
                    {v.ean ? ` · ${t.variants.ean}: ${v.ean}` : ""}
                  </p>
                  <p>
                    <span className="font-semibold">{formatMoney(v.price?.amount)}</span>
                    {v.compareAtPrice ? <span className="ml-2 text-muted-foreground line-through">{formatMoney(v.compareAtPrice.amount)}</span> : null}
                  </p>
                </div>
                <div className="flex flex-wrap gap-2 sm:justify-end">
                  <Button asChild variant="ghost" size="sm">
                    <Link to={`/inventory/items/${v.id}`}>{t.variants.stock}</Link>
                  </Button>
                  <Button asChild variant="ghost" size="sm">
                    <Link to={`/inventory/receive?fragranceId=${fragrance.id}&variantId=${v.id}`}>{t.variants.receive}</Link>
                  </Button>
                  <Button
                    variant="outline"
                    size="sm"
                    aria-label={`${t.common.edit} ${v.sku}`}
                    onClick={() => {
                      setEditing(v);
                      setOpen(true);
                    }}
                  >
                    <Pencil /> {t.common.edit}
                  </Button>
                  {v.isActive ? (
                    <ConfirmDialog
                      trigger={
                        <Button variant="outline" size="sm">
                          {t.variants.deactivate}
                        </Button>
                      }
                      title={t.variants.deactivateConfirm}
                      confirmLabel={t.variants.deactivate}
                      destructive
                      onConfirm={() => toggle(v)}
                    />
                  ) : (
                    <Button variant="outline" size="sm" disabled={setActive.isPending} onClick={() => void toggle(v).catch(() => undefined)}>
                      {t.variants.activate}
                    </Button>
                  )}
                </div>
              </li>
            ))}
          </ul>
        )}
      </CardContent>
      <VariantDialog fragranceId={fragrance.id} variant={editing} open={open} onOpenChange={setOpen} />
    </Card>
  );
}
