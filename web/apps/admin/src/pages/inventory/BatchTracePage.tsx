import { Button, Card, CardContent, CardHeader, CardTitle, EmptyState, ErrorState, formatMoney, LoadingState, TextField } from "@perfumeshop/ui";
import { ScanSearch } from "lucide-react";
import { useState } from "react";
import { Link, useSearchParams } from "react-router";
import { PageHeader } from "@/components/PageHeader";
import { useBatchTrace } from "@/features/inventory/api";
import { formatDate, formatDateTime, label, t } from "@/i18n";
import { describeError } from "@/lib/problem";

export function BatchTracePage() {
  const [params, setParams] = useSearchParams();
  const code = params.get("code") ?? "";
  const [text, setText] = useState(code);
  const trace = useBatchTrace(code);
  const tooShort = text.trim().length > 0 && text.trim().length < 2;

  return (
    <>
      <PageHeader title={t.batchTrace.title} description={t.batchTrace.intro} />
      <form
        className="mb-4 flex flex-col gap-3 sm:flex-row sm:items-end"
        onSubmit={(e) => {
          e.preventDefault();
          if (text.trim().length >= 2) {
            setParams({ code: text.trim() });
          }
        }}
      >
        <TextField className="flex-1 sm:max-w-sm" label={t.batchTrace.code} value={text} onChange={(e) => setText(e.target.value)} error={tooShort ? t.batchTrace.codeRequired : undefined} autoCapitalize="characters" />
        <Button type="submit" disabled={text.trim().length < 2}>
          <ScanSearch /> {t.batchTrace.searchButton}
        </Button>
      </form>

      {!code ? null : trace.isLoading ? (
        <LoadingState />
      ) : trace.error ? (
        <ErrorState message={describeError(trace.error)} onRetry={() => void trace.refetch()} />
      ) : (trace.data ?? []).length === 0 ? (
        <EmptyState title={t.batchTrace.noResults} />
      ) : (
        <div className="grid gap-4">
          {(trace.data ?? []).map((result) => (
            <Card key={result.batch.id}>
              <CardHeader>
                <CardTitle>
                  {result.batch.batchCode} ·{" "}
                  <Link className="underline-offset-4 hover:underline" to={`/inventory/items/${result.variantId}`}>
                    {t.batchTrace.product}
                  </Link>
                </CardTitle>
                <span className="text-sm text-muted-foreground">
                  {result.batch.quantityRemaining} / {result.batch.quantityReceived}
                </span>
              </CardHeader>
              <CardContent className="grid gap-4">
                <dl className="grid gap-2 text-sm sm:grid-cols-2 lg:grid-cols-4">
                  <div>
                    <dt className="text-muted-foreground">{t.stockItem.supplier}</dt>
                    <dd className="font-medium">{result.batch.supplierName}</dd>
                  </div>
                  <div>
                    <dt className="text-muted-foreground">{t.stockItem.invoice}</dt>
                    <dd className="font-medium">
                      {result.batch.supplierInvoiceNumber} · {formatDate(result.batch.supplierInvoiceDate)}
                    </dd>
                  </div>
                  <div>
                    <dt className="text-muted-foreground">{t.stockItem.received}</dt>
                    <dd className="font-medium">{formatDate(result.batch.receivedOn)}</dd>
                  </div>
                  <div>
                    <dt className="text-muted-foreground">{t.stockItem.unitCost}</dt>
                    <dd className="font-medium">{formatMoney(result.batch.unitCost)}</dd>
                  </div>
                </dl>
                <div className="grid gap-2">
                  <p className="text-sm font-medium">{t.batchTrace.shippedTo}</p>
                  {result.shippedTo.length === 0 ? (
                    <p className="text-sm text-muted-foreground">{t.batchTrace.notShipped}</p>
                  ) : (
                    <ul className="grid gap-1 text-sm">
                      {result.shippedTo.map((s) => (
                        <li key={s.orderLineId} className="flex flex-wrap justify-between gap-2 border-b border-border py-1.5 last:border-0">
                          <span>
                            {t.batchTrace.order}{" "}
                            <Link className="font-medium underline-offset-4 hover:underline" to={`/orders/${s.orderId}`}>
                              {s.orderNumber ?? s.orderId.slice(0, 8)}
                            </Link>{" "}
                            · {s.quantity} × · {label(t.enums.allocation, s.status)}
                          </span>
                          <span className="text-muted-foreground">
                            {t.batchTrace.committed}: {formatDateTime(s.committedAtUtc)}
                          </span>
                        </li>
                      ))}
                    </ul>
                  )}
                </div>
              </CardContent>
            </Card>
          ))}
        </div>
      )}
    </>
  );
}
