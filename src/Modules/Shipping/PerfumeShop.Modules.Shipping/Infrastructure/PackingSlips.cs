using System.Globalization;
using System.Net;
using System.Text;
using PerfumeShop.Modules.Orders.Contracts;
using PerfumeShop.Modules.Shipping.Domain;
using PerfumeShop.Modules.Shipping.Infrastructure.Couriers;

namespace PerfumeShop.Modules.Shipping.Infrastructure;

/// <summary>Printable packing slip that goes into the parcel (Albanian).</summary>
internal static class PackingSlips
{
    /// <summary>Printed on every slip: the customer may inspect the goods before paying the courier.</summary>
    public const string OpenBeforePaying = "Hape pakon para se të paguash";

    public static string Render(Shipment shipment, OrderSummaryDto order, ShopInfo shop)
    {
        static string E(string? v) => WebUtility.HtmlEncode(v ?? string.Empty);

        var html = new StringBuilder(HtmlPage.Start($"Fletëdërgesa {order.OrderNumber}"));
        html.Append(CultureInfo.InvariantCulture, $"""
            <header>
              <div><h1>{E(shop.Name)}</h1><p>{E(shop.Address)}</p><p>Tel: {E(shop.Phone)}</p></div>
              <div class="right"><p><strong>Porosia:</strong> {E(order.OrderNumber)}</p><p><strong>Data:</strong> {order.PlacedAtUtc:dd.MM.yyyy}</p>
              <p><strong>Korrieri:</strong> {E(shipment.CourierName)}</p><p><strong>Nr. gjurmimit:</strong> {E(shipment.TrackingNumber)}</p></div>
            </header>
            <p><strong>Marrësi:</strong> {E(shipment.Recipient.FullName)} · {E(shipment.Recipient.Phone)}<br>
            {E(shipment.Recipient.Street)}, {E(shipment.Recipient.City)}{(shipment.Recipient.Notes is { } n ? $" ({E(n)})" : string.Empty)}</p>
            <table>
              <thead><tr><th>Produkti</th><th>Madhësia</th><th>SKU</th><th class="num">Sasia</th><th class="num">Çmimi €</th><th class="num">Gjithsej €</th><th>Batch</th></tr></thead>
              <tbody>
            """);
        foreach (var line in order.Lines)
        {
            var size = line.SizeValue is { } v ? FormattableString.Invariant($"{v:0.#} {(line.SizeUnit switch { "Ml" => "ml", "Gram" => "g", _ => "copë" })}") : string.Empty;
            html.Append(CultureInfo.InvariantCulture, $"<tr><td>{E(line.BrandName)} {E(line.FragranceName)}</td><td>{E(size)}</td><td>{E(line.Sku)}</td><td class=\"num\">{line.Quantity}</td><td class=\"num\">{line.UnitPrice:0.00}</td><td class=\"num\">{line.LineTotal:0.00}</td><td>{E(string.Join(", ", line.Batches))}</td></tr>\n");
        }

        html.Append(CultureInfo.InvariantCulture, $"""
              </tbody>
              <tfoot>
                <tr><td colspan="5">Nëntotali</td><td class="num">{order.Subtotal:0.00}</td><td></td></tr>
                <tr><td colspan="5">Transporti</td><td class="num">{order.DeliveryFee:0.00}</td><td></td></tr>
                <tr><td colspan="5">Gjithsej (përfshirë TVSH 18%: {order.VatAmount:0.00} €)</td><td class="num">{order.Total:0.00}</td><td></td></tr>
              </tfoot>
            </table>
            <div class="cod">Për pagesë në dorëzim: {shipment.CodAmount:0.00} €</div>
            <p class="notice">{OpenBeforePaying}</p>
            <p>Pyetje për porosinë? Na telefononi: {E(shop.Phone)}. Faleminderit që zgjodhët {E(shop.Name)}!</p>
            """);
        html.Append(HtmlPage.End);
        return html.ToString();
    }
}
