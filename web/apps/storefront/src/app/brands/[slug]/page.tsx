export default async function BrandPage({ params }: { params: Promise<{ slug: string }> }) {
  const { slug } = await params;

  return (
    <main className="mx-auto max-w-6xl p-6">
      <h1 className="text-2xl font-semibold">Brendi: {slug}</h1>
      {/* TODO: brand info + product grid with filters. */}
    </main>
  );
}
