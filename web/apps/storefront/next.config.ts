import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  // Workspace packages ship TypeScript source.
  transpilePackages: ["@perfumeshop/ui", "@perfumeshop/api-client"],
};

export default nextConfig;
