import { timingSafeEqual } from "node:crypto";
import { revalidateTag } from "next/cache";
import { NextResponse, type NextRequest } from "next/server";
import { CATALOG_TAG } from "@/lib/catalog";
import { revalidateSecret } from "@/lib/config";

/** Cache tags the API may ask to rebuild. Anything else is rejected. */
const allowedTags = new Set([CATALOG_TAG]);

function validSecret(given: string | null): boolean {
  if (!given || revalidateSecret.length < 32) {
    return false; // not configured: never accept
  }

  const a = Buffer.from(given);
  const b = Buffer.from(revalidateSecret);
  return a.length === b.length && timingSafeEqual(a, b);
}

/**
 * POST /api/revalidate with header x-revalidate-secret and body {"tags":["catalog"]}: sent by the API
 * (Catalog's StorefrontRevalidationJob) after every catalog change, so cached pages show it at once.
 */
export async function POST(request: NextRequest) {
  if (!validSecret(request.headers.get("x-revalidate-secret"))) {
    return NextResponse.json({ error: "unauthorized" }, { status: 401 });
  }

  const body = (await request.json().catch(() => null)) as { tags?: unknown } | null;
  const tags = Array.isArray(body?.tags) ? body.tags.filter((tag): tag is string => typeof tag === "string" && allowedTags.has(tag)) : [];
  if (tags.length === 0) {
    return NextResponse.json({ error: "no known tags" }, { status: 400 });
  }

  for (const tag of tags) {
    revalidateTag(tag, "max");
  }

  return NextResponse.json({ revalidated: tags });
}
