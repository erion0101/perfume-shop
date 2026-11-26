import { Badge, Button, Card, CardContent, EmptyState, ErrorState, formatMoney, LoadingState, toast } from "@perfumeshop/ui";
import { Check, MessageSquarePlus, Phone, X } from "lucide-react";
import { useState } from "react";
import { Link } from "react-router";
import { PageHeader } from "@/components/PageHeader";
import { useConfirmOrder, useCustomerHistory, useOrders, type OrderListItem } from "@/features/orders/api";
import { CancelOrderDialog, OrderNoteDialog } from "@/features/orders/OrderDialogs";
import { formatDateTime, t } from "@/i18n";
import { describeError } from "@/lib/problem";
import { RiskBadge } from "./OrdersPage";

const queueFilters = { status: "Pending", from: "", to: "", phone: "", number: "", courier: "", sort: "placed", page: 1, pageSize: 100 };

function History({ customerId }: { customerId: string }) {
  const history = useCustomerHistory(customerId);
  if (!history.data) {
    return null;
  }

  const h = history.data;
  return (
    <dl className="grid grid-cols-4 gap-2 text-center text-xs" aria-label={t.queue.history}>
      {[
        [t.queue.completed, h.completed, "text-emerald-700"],
        [t.queue.refused, h.refused, h.refused > 0 ? "text-destructive font-semibold" : ""],
        [t.queue.cancelledCount, h.cancelled, ""],
        [t.queue.open, h.open, ""],
      ].map(([name, value, tone]) => (
        <div key={name as string} className="rounded-md bg-muted p-1.5">
          <dt className="text-muted-foreground">{name}</dt>
          <dd className={`text-base tabular-nums ${tone}`}>{value}</dd>
        </div>
      ))}
    </dl>
  );
}

function QueueCard({ order }: { order: OrderListItem }) {
  const confirm = useConfirmOrder();
  const [cancelling, setCancelling] = useState(false);
  const [noting, setNoting] = useState(false);

  async function onConfirm() {
    try {
      await confirm.mutateAsync(order.id);
      toast.success(t.queue.confirmed(order.number));
    } catch (error) {
      toast.error(describeError(error));
    }
  }

  return (
    <li>
      <Card>
        <CardContent className="grid gap-3">
          <div className="flex flex-wrap items-start justify-between gap-2">
            <div className="grid gap-0.5">
              <Link to={`/orders/${order.id}`} className="font-semibold underline-offset-4 hover:underline">
                {order.number}
              </Link>
              <span className="text-xs text-muted-foreground">
                {formatDateTime(order.placedAtUtc)}
                {order.confirmEndsAtUtc ? ` · ${t.orders.expires(formatDateTime(order.confirmEndsAtUtc))}` : ""}
              </span>
            </div>
            <span className="text-lg font-semibold tabular-nums">{formatMoney(order.total)}</span>
          </div>

          <div className="flex flex-wrap items-center justify-between gap-2">
            <div className="grid">
              <span className="font-medium">{order.customerName}</span>
              <span className="text-sm text-muted-foreground">
                {order.city} · {order.itemCount} {t.orders.items}
              </span>
            </div>
            {/* One tap to call from a phone. */}
            <Button asChild variant="secondary" size="lg">
              <a href={`tel:${order.customerPhone}`}>
                <Phone /> {order.customerPhone}
              </a>
            </Button>
          </div>

          <div className="flex flex-wrap gap-2">
            {order.customerOrderCount <= 1 ? <Badge tone="info">{t.orders.firstOrder}</Badge> : <Badge>{t.orders.previousOrders(order.customerOrderCount)}</Badge>}
            <RiskBadge refused={order.customerRefusedCount} />
          </div>
          {order.customerOrderCount > 1 ? <History customerId={order.customerId} /> : null}

          <div className="grid grid-cols-3 gap-2">
            <Button size="lg" disabled={confirm.isPending} onClick={() => void onConfirm()} aria-label={`${t.queue.confirm} ${order.number}`}>
              <Check /> {t.queue.confirm}
            </Button>
            <Button size="lg" variant="outline" onClick={() => setCancelling(true)} aria-label={`${t.queue.cancel} ${order.number}`}>
              <X /> {t.queue.cancel}
            </Button>
            <Button size="lg" variant="ghost" onClick={() => setNoting(true)} aria-label={`${t.queue.note} ${order.number}`}>
              <MessageSquarePlus /> {t.queue.note}
            </Button>
          </div>
        </CardContent>
      </Card>
      <CancelOrderDialog order={order} open={cancelling} onOpenChange={setCancelling} />
      <OrderNoteDialog orderId={order.id} open={noting} onOpenChange={setNoting} />
    </li>
  );
}

export function QueuePage() {
  const queue = useOrders(queueFilters);

  return (
    <>
      <PageHeader title={t.queue.title} description={t.queue.intro} />
      {queue.isLoading ? (
        <LoadingState />
      ) : queue.error ? (
        <ErrorState message={describeError(queue.error)} onRetry={() => void queue.refetch()} />
      ) : (queue.data?.items.length ?? 0) === 0 ? (
        <EmptyState title={t.queue.empty} />
      ) : (
        <ul className="grid gap-3 lg:grid-cols-2" aria-label={t.queue.title}>
          {queue.data!.items.map((order) => (
            <QueueCard key={order.id} order={order} />
          ))}
        </ul>
      )}
    </>
  );
}
