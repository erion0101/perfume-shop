import { zodResolver } from "@hookform/resolvers/zod";
import { Button, Card, CardContent, CardHeader, CardTitle, Dialog, ErrorState, formatMoney, LoadingState, StatusBadge, TextareaField, toast } from "@perfumeshop/ui";
import { ArrowLeft, Ban, Phone, ShieldCheck } from "lucide-react";
import { useState } from "react";
import { useForm } from "react-hook-form";
import { Link, useParams } from "react-router";
import { z } from "zod";
import { PageHeader } from "@/components/PageHeader";
import { useBlockCustomer, useCustomer, useCustomerOrders, type CustomerAdmin } from "@/features/customers/api";
import { orderBadges } from "@/features/statuses";
import { formatDate, formatDateTime, t } from "@/i18n";
import { describeError } from "@/lib/problem";
import { customerBadges } from "./CustomersPage";

const reasonSchema = z.object({ reason: z.string().trim().min(3, t.customers.reasonRequired).max(500, t.customers.reasonRequired) });

/** Block or unblock, always with a reason (stored in the audit log). */
export function BlockDialog({ customer, open, onOpenChange }: { customer: CustomerAdmin; open: boolean; onOpenChange: (open: boolean) => void }) {
  const block = useBlockCustomer();
  const blocking = customer.status !== "Blocked";
  const name = customer.fullName ?? customer.phone;
  const { register, handleSubmit, formState, reset } = useForm({ resolver: zodResolver(reasonSchema), defaultValues: { reason: "" } });
  const onSubmit = handleSubmit(async ({ reason }) => {
    try {
      await block.mutateAsync({ id: customer.id, block: blocking, reason: reason.trim() });
      toast.success(blocking ? t.customers.blockedOk : t.customers.unblockedOk);
      reset();
      onOpenChange(false);
    } catch (error) {
      toast.error(describeError(error));
    }
  });

  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
      title={blocking ? t.customers.blockTitle(name) : t.customers.unblockTitle(name)}
      description={blocking ? t.customers.blockHint : undefined}
      footer={
        <Button type="submit" form="block-form" variant={blocking ? "destructive" : "default"} disabled={formState.isSubmitting}>
          {blocking ? t.customers.block : t.customers.unblock}
        </Button>
      }
    >
      <form id="block-form" onSubmit={(e) => void onSubmit(e)} noValidate>
        <TextareaField label={t.customers.reason} required rows={3} error={formState.errors.reason?.message} {...register("reason")} />
      </form>
    </Dialog>
  );
}

export function CustomerDetailPage() {
  const { id = "" } = useParams();
  const customer = useCustomer(id);
  const orders = useCustomerOrders(id);
  const [blocking, setBlocking] = useState(false);

  if (customer.isLoading) {
    return <LoadingState />;
  }

  if (customer.error || !customer.data) {
    return <ErrorState message={describeError(customer.error)} onRetry={() => void customer.refetch()} />;
  }

  const c = customer.data;
  const list = orders.data?.items ?? [];
  const refused = list.filter((o) => o.status === "Refused");

  return (
    <>
      <PageHeader
        back={
          <Link to="/customers" className="inline-flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground">
            <ArrowLeft className="size-4" /> {t.customers.back}
          </Link>
        }
        title={c.fullName ?? t.customers.unknownName}
        description={
          <span className="flex flex-wrap items-center gap-2">
            <StatusBadge status={c.status} map={customerBadges} />
            <a className="inline-flex items-center gap-1 text-primary underline-offset-4 hover:underline" href={`tel:${c.phone}`}>
              <Phone className="size-3.5" /> {c.phone}
            </a>
          </span>
        }
        actions={
          <Button variant={c.status === "Blocked" ? "default" : "destructive"} onClick={() => setBlocking(true)}>
            {c.status === "Blocked" ? <ShieldCheck /> : <Ban />} {c.status === "Blocked" ? t.customers.unblock : t.customers.block}
          </Button>
        }
      />
      <div className="grid gap-4 lg:grid-cols-[1fr_2fr]">
        <div className="grid content-start gap-4">
          <Card>
            <CardHeader>
              <CardTitle>{t.customers.profile}</CardTitle>
            </CardHeader>
            <CardContent className="grid gap-1 text-sm">
              <p>
                {t.customers.email}: {c.email ?? t.common.none}
              </p>
              <p>
                {t.customers.phoneVerified}: {formatDateTime(c.phoneVerifiedAtUtc)}
              </p>
              <p>
                {t.customers.created}: {formatDate(c.createdAtUtc)} · {t.customers.lastLogin}: {formatDateTime(c.lastLoginAtUtc)}
              </p>
              {c.status === "Blocked" ? (
                <p className="mt-2 rounded-md bg-red-50 p-2">
                  {t.customers.blockReason}: {c.blockReason} ({formatDateTime(c.blockedAtUtc)})
                </p>
              ) : null}
            </CardContent>
          </Card>
          <Card>
            <CardHeader>
              <CardTitle>{t.customers.addresses}</CardTitle>
            </CardHeader>
            <CardContent className="grid gap-2 text-sm">
              {c.addresses.length === 0 ? (
                <p className="text-muted-foreground">{t.customers.noAddresses}</p>
              ) : (
                c.addresses.map((a) => (
                  <p key={a.id}>
                    {a.street}, {a.city} ({a.countryCode}){a.notes ? <span className="block text-muted-foreground">{a.notes}</span> : null}
                  </p>
                ))
              )}
            </CardContent>
          </Card>
        </div>
        <div className="grid content-start gap-4">
          <Card>
            <CardHeader>
              <CardTitle>{t.customers.refusalHistory}</CardTitle>
            </CardHeader>
            <CardContent className="grid gap-1 text-sm">
              {refused.length === 0 ? (
                <p className="text-muted-foreground">{t.customers.noRefusals}</p>
              ) : (
                refused.map((o) => (
                  <Link key={o.id} to={`/orders/${o.id}`} className="flex justify-between gap-2 rounded-md bg-red-50 px-2 py-1 hover:underline">
                    <span className="font-medium">{o.number}</span>
                    <span>
                      {formatDate(o.placedAtUtc)} · {formatMoney(o.total)}
                    </span>
                  </Link>
                ))
              )}
            </CardContent>
          </Card>
          <Card>
            <CardHeader>
              <CardTitle>
                {t.customers.orderHistory} ({list.length})
              </CardTitle>
            </CardHeader>
            <CardContent>
              {list.length === 0 ? (
                <p className="text-sm text-muted-foreground">{t.customers.noOrders}</p>
              ) : (
                <ul className="grid gap-1 text-sm" aria-label={t.customers.orderHistory}>
                  {list.map((o) => (
                    <li key={o.id}>
                      <Link to={`/orders/${o.id}`} className="flex flex-wrap items-center justify-between gap-2 rounded-md px-2 py-1 hover:bg-muted">
                        <span className="font-medium">{o.number}</span>
                        <StatusBadge status={o.status} map={orderBadges} />
                        <span className="tabular-nums">
                          {formatDateTime(o.placedAtUtc)} · {formatMoney(o.total)}
                        </span>
                      </Link>
                    </li>
                  ))}
                </ul>
              )}
            </CardContent>
          </Card>
        </div>
      </div>
      <BlockDialog customer={c} open={blocking} onOpenChange={setBlocking} />
    </>
  );
}
