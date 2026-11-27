// Stand-in for https://api.fragella.com/api/v1/ in storefront e2e runs: a small catalog in Fragella's format
// (the six perfumes of the approved home prototype). No real API key or monthly quota is used.
import { createServer } from "node:http";

const port = Number(process.env.FRAGELLA_MOCK_PORT ?? 4011);

const note = (name) => ({ name, imageUrl: null });
const item = (id, brand, name, { oil = "Eau de Parfum", gender = "unisex", year = "2022", accords, top, middle, base, longevity = "Long Lasting", sillage = "Strong" }) => ({
  _id: id,
  Name: name,
  Brand: brand,
  Year: year,
  Country: "United Arab Emirates",
  Gender: gender,
  Longevity: longevity,
  Sillage: sillage,
  OilType: oil,
  "General Notes": [...top, ...middle, ...base].map((n) => n.toLowerCase()),
  "Main Accords": accords.map(([a]) => a),
  "Main Accords Percentage": Object.fromEntries(accords.map(([a, level]) => [a, level])),
  Notes: { Top: top.map(note), Middle: middle.map(note), Base: base.map(note) },
});

export const catalog = {
  khamrah: item("sf-lattafa-khamrah", "Lattafa", "Khamrah", {
    accords: [["sweet", "Dominant"], ["warm spicy", "Dominant"], ["amber", "Prominent"], ["vanilla", "Prominent"]],
    top: ["Cinnamon", "Nutmeg", "Bergamot"], middle: ["Dates", "Praline", "Tuberose"], base: ["Vanilla", "Tonka Bean", "Amberwood", "Myrrh"],
  }),
  yara: item("sf-lattafa-yara", "Lattafa", "Yara", {
    gender: "women", longevity: "Moderate", sillage: "Moderate",
    accords: [["sweet", "Dominant"], ["powdery", "Prominent"], ["fruity", "Prominent"], ["vanilla", "Moderate"]],
    top: ["Orchid", "Heliotrope", "Tangerine"], middle: ["Tropical Fruits", "Gourmand Accord"], base: ["Vanilla", "Musk", "Sandalwood"],
  }),
  cdni: item("sf-armaf-cdni", "Armaf", "Club de Nuit Intense Man", {
    oil: "Eau de Toilette", gender: "men", year: "2015",
    accords: [["citrus", "Dominant"], ["fresh", "Prominent"], ["woody", "Prominent"], ["smoky", "Moderate"]],
    top: ["Lemon", "Pineapple", "Bergamot"], middle: ["Birch", "Jasmine", "Rose"], base: ["Musk", "Ambergris", "Vanilla"],
  }),
  hawas: item("sf-rasasi-hawas", "Rasasi", "Hawas for Him", {
    gender: "men", year: "2015",
    accords: [["fresh", "Dominant"], ["aquatic", "Prominent"], ["fruity", "Moderate"], ["aromatic", "Moderate"]],
    top: ["Apple", "Bergamot", "Lemon"], middle: ["Cardamom", "Orange Blossom", "Plum"], base: ["Ambergris", "Musk", "Driftwood"],
  }),
  asad: item("sf-lattafa-asad", "Lattafa", "Asad", {
    gender: "men", year: "2021",
    accords: [["warm spicy", "Dominant"], ["tobacco", "Prominent"], ["vanilla", "Moderate"], ["amber", "Moderate"]],
    top: ["Black Pepper", "Pineapple", "Tobacco"], middle: ["Coffee", "Iris", "Patchouli"], base: ["Vanilla", "Amber", "Benzoin"],
  }),
  amethyst: item("sf-lattafa-amethyst", "Lattafa", "Bade'e Al Oud Amethyst", {
    year: "2021",
    accords: [["oud", "Dominant"], ["rose", "Prominent"], ["woody", "Prominent"], ["musky", "Moderate"]],
    top: ["Pink Pepper", "Bergamot"], middle: ["Rose", "Agarwood"], base: ["Musk", "Patchouli", "Vanilla"],
  }),
};

createServer((request, response) => {
  const url = new URL(request.url ?? "/", `http://127.0.0.1:${port}`);
  if (url.pathname === "/health") {
    response.writeHead(200).end("ok");
    return;
  }

  if (request.headers["x-api-key"] !== "e2e-fragella-key") {
    response.writeHead(401, { "Content-Type": "application/json" }).end(JSON.stringify({ error: "invalid api key" }));
    return;
  }

  if (url.pathname === "/api/v1/fragrances") {
    const query = (url.searchParams.get("search") ?? "").toLowerCase();
    const hits = Object.entries(catalog).filter(([key]) => query.includes(key)).map(([, value]) => value);
    response.writeHead(200, { "Content-Type": "application/json" }).end(JSON.stringify(hits));
    return;
  }

  response.writeHead(404, { "Content-Type": "application/json" }).end("{}");
}).listen(port, "127.0.0.1", () => console.log(`Storefront Fragella mock on http://127.0.0.1:${port}`));
