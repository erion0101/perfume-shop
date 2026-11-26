import { Badge, Button, Card, CardContent, CardHeader, CardTitle, DataTable, ErrorState, formatMoney, LoadingState, Select, StatusBadge, toast } from "@perfumeshop/ui";
import { ArrowLeft, RotateCcw } from "lucide-react";
import { Link, useNavigate, useParams } from "react-router";
import { PrintButton } from "@/components/DocumentDialog";
import { PageHeader } from "@/components/PageHeader";
import { SearchInput } from "@/components/SearchInput";
import { useReceipt, useReceipts, useRetryReceipt, type Receipt } from "@/features/payments/api";
import { receiptBadges } from "@/features/statuses";
import { formatDateTime, label, t } from "@/i18n";
import { describeError } from "@/lib/problem";
import { useSearchState } from "@/lib/use-search-state";

const defaults = { status: "", kind: "", orderNumber: "", page: "1" };
const pageSize = 50;

export function RetryReceiptButton({ receipt }: { receipt: Receipt }) {
  const retry = useRetryReceipt();
  if (receipt.status !== "Failed" && receipt.status !== "Pending") {
    return null;
  }

  return (
    <Button
      variant="outline"
      size="sm"
      disabled={retry.isPending}
      onClick={(e) => {
        e.stopPropagation();
        void retry.mutateAsync(receipt.id).then(
          (result) => (result.status === "Issued" ? toast.success(t.receipts.retried) : toast.error(`${t.receipts.retryFailed} ${result.lastError ?? ""}`)),
          (error: unknown) => toast.error(describeError(error)),
        );
      }}
    >
      <RotateCcw /> {t.receipts.retry}
    </Button>
  );
}

export function ReceiptsPage() {
  const navigate = useNavigate();
  const [state, set] = useSearchState(defaults);
  const page = Number(state.page) || 1;
  const receipts = useReceipts({ ...state, page, pageSize });

  return (
    <>
      <PageHeader title={t.receipts.title} />
      <DataTable<Receipt>
        caption={t.receipts.title}
        toolbar={
          <>
            <SearchInput value={state.orderNumber} onChange={(orderNumber) => set({ orderNumber })} placeholder={t.payments.orderFilter} className="sm:max-w-44" />
            <Select className="w-full sm:w-44" aria-label={t.receipts.status} value={state.status} onChange={(e) => set({ status: e.target.value })}>
              <option value="">{t.receipts.allStatuses}</option>
              {Object.entries(t.enums.receiptStatus).map(([value, text]) => (
                <option key={value} value={value}>
                  {text}
                </option>
              ))}
            </Select>
            <Select className="w-full sm:w-40" aria-label={t.receipts.kind} value={state.kind} onChange={(e) => set({ kind: e.target.value })}>
              <option value="">{t.receipts.allKinds}</option>
              {Object.entries(t.enums.receiptKind).map(([value, text]) => (
                <option key={value} value={value}>
                  {text}
                </option>
              ))}
            </Select>
          </>
        }
        columns={[
          { key: "number", header: t.receipts.number, cell: (r) => <span className="font-medium">{r.internalNumber}</span> },
          { key: "kind", header: t.receipts.kind, cell: (r) => label(t.enums.receiptKind, r.kind) },
          { key: "status", header: t.receipts.status, cell: (r) => <StatusBadge status={r.status} map={receiptBadges} /> },
          { key: "order", header: t.receipts.order, cell: (r) => r.orderNumber },
          { key: "total", header: t.receipts.total, cell: (r) => formatMoney(r.total), className: "text-right tabular-nums" },
          { key: "issued", header: t.receipts.issued, cell: (r) => formatDateTime(r.issuedAtUtc ?? r.createdAtUtc), hideOnMobile: true },
          { key: "retry", header: t.common.actions, cell: (r) => <RetryReceiptButton receipt={r} /> },
        ]}
        rows={receipts.data?.items}
        rowKey={(r) => r.id}
        total={receipts.data?.totalCount ?? 0}
        page={page}
        pageSize={pageSize}
        onPageChange={(next) => set({ page: String(next) })}
        isLoading={receipts.isLoading}
        error={receipts.error ? describeError(receipts.error) : null}
        onRetry={() => void receipts.refetch()}
        emptyTitle={t.receipts.empty}
        onRowClick={(r) => navigate(`/payments/receipts/${r.id}`)}
      />
    </>
  );
}

export function ReceiptDetailPage() {
  const { id = "" } = useParams();
  const receipt = useReceipt(id);
  if (receipt.isLoading) {
    return <LoadingState />;
  }

  if (receipt.error || !receipt.data) {
    return <ErrorState message={describeError(receipt.error)} onRetry={() => void receipt.refetch()} />;
  }

  const r = receipt.data;
  return (
    <>
      <PageHeader
        back={
          <Link to="/payments/receipts" className="inline-flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground">
            <ArrowLeft className="size-4" /> {t.receipts.back}
          </Link>
        }
        title={t.receipts.detailTitle(r.internalNumber)}
        description={
          <span className="flex flex-wrap items-center gap-2">
            <StatusBadge status={r.status} map={receiptBadges} /> {label(t.enums.receiptKind, r.kind)} ·{" "}
            <Link className="underline-offset-4 hover:underline" to={`/orders/${r.orderId}`}>
              {r.orderNumber}
            </Link>
            {!r.isFiscal && r.status === "Issued" ? <Badge tone="danger">{t.receipts.nonFiscal}</Badge> : null}
          </span>
        }
        actions={
          <>
            <RetryReceiptButton receipt={r} />
            <PrintButton document={{ kind: "receipt", receiptId: r.id }} title={t.receipts.detailTitle(r.internalNumber)} label={t.receipts.print} />
          </>
        }
      />
      <div className="grid gap-4 lg:grid-cols-[2fr_1fr]">
        <Card>
          <CardHeader>
            <CardTitle>{t.receipts.lines}</CardTitle>
          </CardHeader>
          <CardContent className="overflow-x-auto">
            <table className="w-full min-w-[32rem] text-sm">
              <tbody>
                {r.lines.map((l, i) => (
                  <tr key={i} className="border-b border-border last:border-0">
                    <td className="py-1.5">
                      {l.description}
                      {l.batches ? <span className="block text-xs text-muted-foreground">Batch: {l.batches}</span> : null}
                    </td>
                    <td className="py-1.5 text-right tabular-nums">
                      {l.quantity} × {formatMoney(l.unitPrice)}
                    </td>
                    <td className="py-1.5 text-right font-medium tabular-nums">{formatMoney(l.lineTotal)}</td>
                  </tr>
                ))}
                <tr>
                  <td className="pt-2 font-semibold">{t.receipts.total}</td>
                  <td />
                  <td className="pt-2 text-right font-semibold tabular-nums">{formatMoney(r.total)}</td>
                </tr>
              </tbody>
            </table>
            <p className="mt-3 text-sm font-medium">{t.receipts.vatBreakdown}</p>
            {r.vatBreakdown.map((v) => (
              <p key={v.rate} className="text-sm tabular-nums">
                {t.receipts.rate} {Math.round(v.rate * 100)}% · {t.receipts.net} {formatMoney(v.net)} · {t.receipts.vat} {formatMoney(v.vat)} · {t.receipts.gross} {formatMoney(v.gross)}
              </p>
            ))}
          </CardContent>
        </Card>
        <Card>
          <CardHeader>
            <CardTitle>{t.receipts.attempts}</CardTitle>
          </CardHeader>
          <CardContent className="grid gap-3 text-sm">
            <p>
              {t.receipts.fiscalNumber}: <strong>{r.fiscalNumber ?? t.common.none}</strong>
            </p>
            {r.originalReceiptId ? (
              <p>
                {t.receipts.original}:{" "}
                <Link className="underline-offset-4 hover:underline" to={`/payments/receipts/${r.originalReceiptId}`}>
                  {r.originalInternalNumber}
                </Link>
              </p>
            ) : null}
            {r.lastError ? <p className="text-destructive">{t.receipts.lastError}: {r.lastError}</p> : null}
            {r.voidReason ? <p>{t.receipts.voidReason}: {r.voidReason}</p> : null}
            <ol className="grid gap-2">
              {r.attempts.map((a, i) => (
                <li key={i} className="rounded-md border border-border p-2">
                  <p className="flex justify-between gap-2">
                    <span>{formatDateTime(a.atUtc)}</span>
                    <Badge tone={a.succeeded ? "success" : "danger"}>{a.succeeded ? t.receipts.attemptOk : t.receipts.attemptFailed}</Badge>
                  </p>
                  <p className="text-xs text-muted-foreground">
                    {t.receipts.provider}: {a.provider}
                    {a.error ? ` · ${a.error}` : ""}
                  </p>
                </li>
              ))}
            </ol>
          </CardContent>
        </Card>
      </div>
    </>
  );
}
