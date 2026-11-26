export default async function ProductPage({ params }: { params: Promise<{ slug: string }> }) {
  const { slug } = await params;

  return (
    <main className="mx-auto max-w-6xl p-6">
      <h1 className="text-2xl font-semibold">Produkti: {slug}</h1>
      {/* TODO: gallery, variants (ml), price, note pyramid, accords, Schema.org Product/Offer. */}
    </main>
  );
}
