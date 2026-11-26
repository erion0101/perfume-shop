import type { Metadata } from "next";
import type { ReactNode } from "react";
import "./globals.css";

export const metadata: Metadata = {
  title: {
    default: "Parfume Arabe",
    template: "%s | Parfume Arabe",
  },
  description: "Parfume origjinale arabe në Kosovë. Pagesa në dorëzim.",
};

export default function RootLayout({ children }: { children: ReactNode }) {
  return (
    <html lang="sq">
      <body className="min-h-screen bg-white font-sans text-oud antialiased">{children}</body>
    </html>
  );
}
