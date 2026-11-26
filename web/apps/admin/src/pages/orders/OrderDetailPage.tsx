import { Button, Card, CardContent, CardHeader, CardTitle, ErrorState, formatMoney, LoadingState, StatusBadge, toast } from "@perfumeshop/ui";
import { ArrowLeft, Check, MessageSquarePlus, Phone, Truck, X } from "lucide-react";
import { useState, type ReactNode } from "react";
import { Link, useParams } from "react-router";
import { DocumentDialog, PrintButton } from "@/components/DocumentDialog";
import { PageHeader } from "@/components/PageHeader";
import { useConfirmOrder, useCustomerHistory, useOrder, type OrderAdmin } from "@/features/orders/api";
import { CancelOrderDialog, CreateShipmentDialog, OrderNoteDialog } from "@/features/orders/OrderDialogs";
import { usePaymentForOrder } from "@/features/payments/api";
import { useShipmentForOrder, type Shipment } from "@/features/shipping/api";
import { orderBadges, paymentBadges, receiptBadges, shipmentBadges } from "@/features/statuses";
import { formatDateTime, label, t } from "@/i18n";
import { describeError } from "@/lib/problem";
import { RiskBadge } from "./OrdersPage";

function Row({ name, children }: { name: string; children: ReactNode }) {
  return (
    <div className="flex justify-between gap-3 py-1 text-sm">
      <dt className="text-muted-foreground">{name}</dt>
      <dd className="text-right">{children}</dd>
    </div>
  );
}

function PaymentCard({ order, shipment }: { order: OrderAdmin; shipment: Shipment | null }) {
  // The payment appears, and the receipt is issued, by background jobs a few seconds later: keep looking until then.
  const payment = usePaymentForOrder(order.order.id, (data) => !data || (shipment !== null && !data.receipts.some((r) => r.kind === "Sale" && r.status !== "Pending")));
  const p = payment.data?.payment;

  return (
    <Card>
      <CardHeader>
        <CardTitle>{t.orderDetail.payment}</CardTitle>
        {p ? <StatusBadge status={p.status} map={paymentBadges} /> : null}
      </CardHeader>
      <CardContent>
        {!p ? (
          <p className="text-sm text-muted-foreground">{t.orderDetail.noPayment}</p>
        ) : (
          <dl>
            <Row name={t.payments.method}>{t.orderDetail.paymentMethod}</Row>
            <Row name={t.payments.expected}>{formatMoney(p.expectedAmount)}</Row>
            <Row name={t.payments.collected}>{formatMoney(p.collectedAmount)}</Row>
            <Row name={t.payments.fee}>{formatMoney(p.courierFee)}</Row>
            <Row name={t.payments.net}>{formatMoney(p.netReceived)}</Row>
            {p.settlementId ? (
              <Row name={t.payments.settlement}>
                <Link className="underline-offset-4 hover:underline" to={`/shipping/settlements/${p.settlementId}`}>
                  {p.settlementReference}
                </Link>
              </Row>
            ) : null}
          </dl>
        )}
        <div className="mt-3 grid gap-2 border-t border-border pt-3">
          <p className="text-sm font-medium">{t.orderDetail.receipts}</p>
          {(payment.data?.receipts.length ?? 0) === 0 ? (
            <p className="text-sm text-muted-foreground">{t.orderDetail.noReceipts}</p>
          ) : (
            payment.data!.receipts.map((r) => (
              <div key={r.id} className="flex flex-wrap items-center justify-between gap-2 text-sm">
                <Link className="font-medium underline-offset-4 hover:underline" to={`/payments/receipts/${r.id}`}>
                  {label(t.enums.receiptKind, r.kind)} {r.internalNumber}
                </Link>
                <StatusBadge status={r.status} map={receiptBadges} />
                {r.status === "Issued" ? <PrintButton document={{ kind: "receipt", receiptId: r.id }} title={`${t.receipts.title} ${r.internalNumber}`} label={t.shipments.printReceipt} /> : null}
              </div>
            ))
          )}
        </div>
      </CardContent>
    </Card>
  );
}

export function OrderDetailPage() {
  const { id = "" } = useParams();
  const order = useOrder(id);
  const confirm = useConfirmOrder();
  const shipment = useShipmentForOrder(order.data?.order.number);
  const history = useCustomerHistory(order.data?.customerId);
  const [cancelling, setCancelling] = useState(false);
  const [noting, setNoting] = useState(false);
  const [shipping, setShipping] = useState(false);
  const [slipFor, setSlipFor] = useState<Shipment | null>(null);

  if (order.isLoading) {
    return <LoadingState />;
  }

  if (order.error || !order.data) {
    return <ErrorState message={describeError(order.error)} onRetry={() => void order.refetch()} />;
  }

  const a = order.data;
  const o = a.order;
  const canCancel = o.status === "Pending" || o.status === "Confirmed";

  return (
    <>
      <PageHeader
        back={
          <Link to="/orders" className="inline-flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground">
            <ArrowLeft className="size-4" /> {t.orderDetail.back}
          </Link>
        }
        title={o.number}
        description={
          <span className="flex flex-wrap items-center gap-2">
            <StatusBadge status={o.status} map={orderBadges} /> {formatDateTime(o.placedAtUtc)}
            {o.confirmEndsAtUtc ? ` · ${t.orders.expires(formatDateTime(o.confirmEndsAtUtc))}` : ""}
          </span>
        }
        actions={
          <>
            {o.status === "Pending" ? (
              <Button
                disabled={confirm.isPending}
                onClick={() =>
                  void confirm.mutateAsync(o.id).then(
                    () => toast.success(t.queue.confirmed(o.number)),
                    (error: unknown) => toast.error(describeError(error)),
                  )
                }
              >
                <Check /> {t.queue.confirm}
              </Button>
            ) : null}
            {o.status === "Confirmed" && !shipment.data ? (
              <Button onClick={() => setShipping(true)}>
                <Truck /> {t.orderDetail.createShipment}
              </Button>
            ) : null}
            {canCancel ? (
              <Button variant="outline" onClick={() => setCancelling(true)}>
                <X /> {t.queue.cancel}
              </Button>
            ) : null}
            <Button variant="ghost" onClick={() => setNoting(true)}>
              <MessageSquarePlus /> {t.orderDetail.addNote}
            </Button>
          </>
        }
      />

      <div className="grid gap-4 lg:grid-cols-[2fr_1fr]">
        <div className="grid content-start gap-4">
          <Card>
            <CardHeader>
              <CardTitle>{t.orderDetail.lines}</CardTitle>
            </CardHeader>
            <CardContent className="grid gap-3">
              {o.lines.map((l) => (
                <div key={l.id} className="grid gap-1 border-b border-border pb-3 text-sm last:border-0 last:pb-0">
                  <div className="flex flex-wrap justify-between gap-2">
                    <span className="font-medium">
                      {l.brandName} {l.fragranceName}
                      {l.sizeValue ? ` ${l.sizeValue} ${label(t.enums.sizeUnit, l.sizeUnit)}` : ""}
                    </span>
                    <span className="tabular-nums">
                      {l.quantity} × {formatMoney(l.unitPrice)} = <strong>{formatMoney(l.lineTotal)}</strong>
                    </span>
                  </div>
                  <span className="text-xs text-muted-foreground">
                    {l.sku} · {label(t.enums.variantType, l.variantType)} · {t.orderDetail.batches}:{" "}
                    {l.batches.length === 0
                      ? t.orderDetail.noBatches
                      : l.batches.map((b, i) => (
                          <span key={b.batchCode}>
                            {i > 0 ? ", " : ""}
                            <Link className="underline-offset-4 hover:underline" to={`/inventory/trace?code=${encodeURIComponent(b.batchCode)}`}>
                              {b.batchCode}
                            </Link>
                            ×{b.quantity}
                          </span>
                        ))}
                  </span>
                </div>
              ))}
              <dl className="border-t border-border pt-2">
                <Row name={t.orderDetail.subtotal}>{formatMoney(o.subtotal)}</Row>
                <Row name={t.orderDetail.delivery}>{formatMoney(o.deliveryFee)}</Row>
                <Row name={t.orderDetail.vat}>{formatMoney(o.vatAmount)}</Row>
                <Row name={t.orderDetail.total}>
                  <strong className="text-base">{formatMoney(o.total)}</strong>
                </Row>
              </dl>
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle>{t.orderDetail.timeline}</CardTitle>
            </CardHeader>
            <CardContent>
              <ol className="grid gap-2 text-sm">
                {o.timeline.map((e, i) => (
                  <li key={`${e.atUtc}-${i}`} className="flex flex-wrap gap-x-2">
                    <span className="text-muted-foreground tabular-nums">{formatDateTime(e.atUtc)}</span>
                    <StatusBadge status={e.to} map={orderBadges} />
                    <span className="text-muted-foreground">{label(t.orderDetail.actorKind, e.actorKind)}</span>
                    {e.note ? <span>· {e.note}</span> : null}
                  </li>
                ))}
              </ol>
              {a.cancellationReason ? <p className="mt-2 text-sm">{t.orderDetail.cancellationReason}: {a.cancellationReason}</p> : null}
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle>{t.orderDetail.notes}</CardTitle>
            </CardHeader>
            <CardContent>
              {a.notes.length === 0 ? (
                <p className="text-sm text-muted-foreground">{t.orderDetail.noNotes}</p>
              ) : (
                <ul className="grid gap-2 text-sm">
                  {a.notes.map((n, i) => (
                    <li key={`${n.atUtc}-${i}`}>
                      <span className="text-xs text-muted-foreground">{formatDateTime(n.atUtc)}</span>
                      <p>{n.text}</p>
                    </li>
                  ))}
                </ul>
              )}
            </CardContent>
          </Card>
        </div>

        <div className="grid content-start gap-4">
          <Card>
            <CardHeader>
              <CardTitle>{t.orderDetail.address}</CardTitle>
              <RiskBadge refused={history.data?.refused ?? 0} />
            </CardHeader>
            <CardContent className="grid gap-2 text-sm">
              <p className="font-medium">{o.customerName}</p>
              <a className="inline-flex items-center gap-1 font-medium text-primary underline-offset-4 hover:underline" href={`tel:${o.customerPhone}`}>
                <Phone className="size-4" /> {o.customerPhone}
              </a>
              <p>
                {o.deliveryAddress.street}, {o.deliveryAddress.city} ({o.deliveryAddress.countryCode})
              </p>
              {o.deliveryAddress.notes ? <p className="text-muted-foreground">{o.deliveryAddress.notes}</p> : null}
              {o.customerNote ? (
                <p className="rounded-md bg-muted p-2">
                  {t.queue.customerNote}: {o.customerNote}
                </p>
              ) : null}
              {history.data ? (
                <p className="text-xs text-muted-foreground">
                  {t.orders.previousOrders(history.data.total)} · {t.queue.completed}: {history.data.completed} · {t.queue.refused}: {history.data.refused} · {t.queue.cancelledCount}:{" "}
                  {history.data.cancelled}
                </p>
              ) : null}
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle>{t.orderDetail.shipment}</CardTitle>
              {shipment.data ? <StatusBadge status={shipment.data.status} map={shipmentBadges} /> : null}
            </CardHeader>
            <CardContent className="grid gap-2 text-sm">
              {!shipment.data ? (
                <p className="text-muted-foreground">{t.orderDetail.noShipment}</p>
              ) : (
                <>
                  <p>
                    {shipment.data.courierName} · <strong>{shipment.data.trackingNumber}</strong>
                  </p>
                  <div className="flex flex-wrap gap-2">
                    <Button asChild variant="outline" size="sm">
                      <Link to={`/shipping/shipments/${shipment.data.id}`}>{t.orderDetail.openShipment}</Link>
                    </Button>
                    <PrintButton document={{ kind: "packing-slip", shipmentId: shipment.data.id }} title={t.shipments.packingSlip} label={t.shipments.printSlip} />
                  </div>
                </>
              )}
            </CardContent>
          </Card>

          <PaymentCard order={a} shipment={shipment.data} />
        </div>
      </div>

      <CancelOrderDialog order={o} open={cancelling} onOpenChange={setCancelling} />
      <OrderNoteDialog orderId={o.id} open={noting} onOpenChange={setNoting} />
      {shipping ? <CreateShipmentDialog order={a} open={shipping} onOpenChange={setShipping} onCreated={setSlipFor} /> : null}
      {slipFor ? (
        <DocumentDialog document={{ kind: "packing-slip", shipmentId: slipFor.id }} title={`${t.shipments.packingSlip} · ${slipFor.trackingNumber}`} open onOpenChange={(open) => !open && setSlipFor(null)} />
      ) : null}
    </>
  );
}
