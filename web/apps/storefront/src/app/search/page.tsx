export default async function SearchPage({
  searchParams,
}: {
  searchParams: Promise<{ q?: string }>;
}) {
  const { q } = await searchParams;

  return (
    <main className="mx-auto max-w-6xl p-6">
      <h1 className="text-2xl font-semibold">Kërko{q ? `: ${q}` : ""}</h1>
      {/* TODO: Meilisearch results + filters (brand, gender, note, family, price). */}
    </main>
  );
}
