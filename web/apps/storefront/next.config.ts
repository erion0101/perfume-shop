import type { NextConfig } from "next";

/** Server-side only (never NEXT_PUBLIC_): the browser talks to the API through the same-origin rewrites below. */
const apiUrl = process.env.API_URL ?? "http://localhost:5080";
const isProduction = process.env.NODE_ENV === "production";

/** Product photos: uploaded through the admin and served by the API (/media) or, later, Cloudflare R2. */
const imageHosts = [new URL(apiUrl), ...(process.env.IMAGE_HOSTS ?? "").split(",").filter(Boolean).map((h) => new URL(h))];

const securityHeaders = [
  { key: "X-Content-Type-Options", value: "nosniff" },
  { key: "Referrer-Policy", value: "strict-origin-when-cross-origin" },
  { key: "X-Frame-Options", value: "DENY" },
  { key: "Permissions-Policy", value: "camera=(), microphone=(), geolocation=(), payment=(), usb=()" },
  { key: "Cross-Origin-Opener-Policy", value: "same-origin" },
  ...(isProduction ? [{ key: "Strict-Transport-Security", value: "max-age=63072000; includeSubDomains; preload" }] : []),
];

const nextConfig: NextConfig = {
  // Workspace packages ship TypeScript source.
  transpilePackages: ["@perfumeshop/ui", "@perfumeshop/api-client"],
  poweredByHeader: false,
  images: {
    formats: ["image/avif", "image/webp"],
    remotePatterns: imageHosts.map((u) => ({ protocol: u.protocol.replace(":", "") as "http" | "https", hostname: u.hostname, port: u.port, pathname: "/**" })),
  },
  async headers() {
    return [{ source: "/:path*", headers: securityHeaders }];
  },
  /** Public API calls from the browser (cart quote, search suggestions, mood filter) stay same-origin: no CORS. */
  async rewrites() {
    return [
      { source: "/api/catalog/:path*", destination: `${apiUrl}/api/catalog/:path*` },
      { source: "/api/cart/:path*", destination: `${apiUrl}/api/cart/:path*` },
      { source: "/media/:path*", destination: `${apiUrl}/media/:path*` },
    ];
  },
};

export default nextConfig;
