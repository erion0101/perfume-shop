export default async function OrderPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;

  return (
    <main className="mx-auto max-w-3xl p-6">
      <h1 className="text-2xl font-semibold">Porosia #{id}</h1>
      {/* TODO: order confirmation and status (New → Confirmed → Shipped → Delivered). */}
    </main>
  );
}
