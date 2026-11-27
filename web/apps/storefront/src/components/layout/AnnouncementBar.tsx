import { formatMoney } from "@perfumeshop/ui";
import { getDeliveryOptions, orEmpty } from "@/lib/catalog";
import { t } from "@/i18n";

/** The gold strip: COD and the real free-delivery threshold (from Orders, never hard-coded). */
export async function AnnouncementBar() {
  const options = await orEmpty(getDeliveryOptions(), []);
  const kosovo = options.find((o) => o.countryCode === "XK");
  return (
    <div className="bg-gold px-4 py-[9px] text-center text-[13px] font-medium text-ground">
      {t.announcement.cod}{" "}
      {kosovo ? (
        <>
          {t.announcement.delivery("Kosovën")}
          {kosovo.freeDeliveryFrom != null ? `, ${t.announcement.freeFrom(formatMoney(kosovo.freeDeliveryFrom))}` : "."}
        </>
      ) : null}
    </div>
  );
}
