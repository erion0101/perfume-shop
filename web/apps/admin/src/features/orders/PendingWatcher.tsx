import { Badge, Button, cn, toast } from "@perfumeshop/ui";
import { BellRing, Volume2, VolumeX } from "lucide-react";
import { useEffect, useRef, useState } from "react";
import { Link, useNavigate } from "react-router";
import { t } from "@/i18n";
import { beep, soundPreference } from "@/lib/beep";
import { useOrders } from "./api";

const pendingFilters = { status: "Pending", from: "", to: "", phone: "", number: "", courier: "", sort: "-placed", page: 1, pageSize: 100 };

/**
 * Top-bar watcher: Pending orders are polled every 30 s; a new one shows a toast (and beeps when sound is on),
 * on whatever page staff are.
 */
export function PendingWatcher() {
  const navigate = useNavigate();
  const pending = useOrders(pendingFilters);
  const seen = useRef<Set<string> | null>(null);
  const [sound, setSound] = useState(soundPreference.get);
  const [flash, setFlash] = useState(false);
  const total = pending.data?.totalCount ?? 0;

  useEffect(() => {
    const ids = pending.data?.items.map((o) => o.id);
    if (!ids) {
      return;
    }

    if (seen.current === null) {
      seen.current = new Set(ids); // first load: nothing is "new"
      return;
    }

    const fresh = ids.filter((id) => !seen.current!.has(id));
    ids.forEach((id) => seen.current!.add(id));
    if (fresh.length > 0) {
      toast.info(t.orders.newPending(fresh.length), { action: { label: t.orders.openQueue, onClick: () => navigate("/orders/queue") } });
      if (soundPreference.get()) {
        beep();
      }

      setFlash(true);
      const timer = window.setTimeout(() => setFlash(false), 4000);
      return () => window.clearTimeout(timer);
    }
  }, [pending.data, navigate]);

  return (
    <div className="flex items-center gap-1">
      <Button asChild variant="ghost" size="sm" className={cn(flash && "animate-pulse bg-amber-100")}>
        <Link to="/orders/queue" aria-label={`${t.nav.queue}: ${total}`} data-testid="pending-count">
          <BellRing />
          <Badge tone={total > 0 ? "warning" : "neutral"}>{total}</Badge>
        </Link>
      </Button>
      <Button
        variant="ghost"
        size="icon"
        aria-pressed={sound}
        aria-label={sound ? t.orders.soundOn : t.orders.soundOff}
        title={t.orders.sound}
        onClick={() => {
          soundPreference.set(!sound);
          setSound(!sound);
          if (!sound) {
            beep(); // also unlocks audio in the browser
          }
        }}
      >
        {sound ? <Volume2 /> : <VolumeX />}
      </Button>
    </div>
  );
}
