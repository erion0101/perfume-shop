# Courier CSV samples

What a courier sends us, in the format `ManualCourierAdapter` reads (semicolon or comma, UTF-8,
decimal comma or point, Albanian or English status words). Used by `../Shipping.http` and by the
Shipping tests (`CourierCsvTests`), so they always parse.

| File | Columns | Imported with |
|---|---|---|
| `courier-status-update.csv` | `tracking_number;status;note` — status: `Dorëzuar`/`Delivered`, `Refuzuar`/`Kthyer`/`Refused`, `Në rrugë`/`InTransit`, `Marrë`/`HandedOver` | `POST /api/admin/shipping/couriers/{id}/status-imports` |
| `courier-remittance.csv` | `tracking_number;collected_amount;fee` (EUR) | `POST /api/admin/shipping/couriers/{id}/settlements?reference=…` |

The tracking numbers are examples: change them to the ones of your test shipments, or use the inline
CSV requests in `Shipping.http`, which fill in your shipment's tracking number and amount.
