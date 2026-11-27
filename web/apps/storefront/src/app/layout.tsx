import type { Metadata, Viewport } from "next";
import { Instrument_Sans, Marcellus } from "next/font/google";
import type { ReactNode } from "react";
import { AnnouncementBar } from "@/components/layout/AnnouncementBar";
import { Footer } from "@/components/layout/Footer";
import { Header } from "@/components/layout/Header";
import { CartDrawer } from "@/features/cart/CartDrawer";
import { CartProvider } from "@/features/cart/CartProvider";
import { t } from "@/i18n";
import { siteUrl } from "@/lib/config";
import "./globals.css";

// Subset to the characters Albanian needs (latin + latin-ext for ë, ç); self-hosted by next/font, no Google request at runtime.
const marcellus = Marcellus({ weight: "400", subsets: ["latin", "latin-ext"], display: "swap", variable: "--font-marcellus" });
const instrument = Instrument_Sans({ weight: ["400", "500", "600"], subsets: ["latin", "latin-ext"], display: "swap", variable: "--font-instrument" });

export const metadata: Metadata = {
  metadataBase: new URL(siteUrl),
  title: { default: `${t.siteName}: parfume arabe origjinale në Kosovë`, template: `%s | ${t.siteName}` },
  description: `${t.tagline}. Lattafa, Armaf, Rasasi, Afnan e të tjera. Paguan kur të vjen porosia.`,
  applicationName: t.siteName,
  openGraph: { siteName: t.siteName, locale: "sq_XK", type: "website" },
};

export const viewport: Viewport = { themeColor: "#0C0A08", colorScheme: "dark" };

export default function RootLayout({ children }: { children: ReactNode }) {
  return (
    <html lang="sq" className={`${marcellus.variable} ${instrument.variable}`}>
      <body className="min-h-dvh antialiased">
        <a href="#main" className="sr-only z-[60] rounded-full bg-gold px-5 py-3 font-semibold text-ground focus:not-sr-only focus:fixed focus:left-4 focus:top-4">
          {t.skipToContent}
        </a>
        <CartProvider>
          <AnnouncementBar />
          <Header />
          <main id="main" tabIndex={-1} className="page-enter outline-none">
            {children}
          </main>
          <Footer />
          <CartDrawer />
        </CartProvider>
      </body>
    </html>
  );
}
