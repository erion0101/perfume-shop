/**
 * Glass colours of the drawn bottle (shown when a product has no photo yet), from the prototype's palette.
 * The mood picks the family of colours; the slug picks one of them, so neighbouring cards differ.
 */
const palettes: Record<string, string[]> = {
  Sweet: ["linear-gradient(160deg,rgba(255,220,160,.5),rgba(140,60,18,.75))", "linear-gradient(160deg,rgba(255,210,220,.55),rgba(190,90,120,.7))"],
  Fresh: ["linear-gradient(160deg,rgba(170,215,255,.5),rgba(30,90,160,.75))", "linear-gradient(160deg,rgba(90,90,90,.6),rgba(10,10,10,.9))"],
  Spicy: ["linear-gradient(160deg,rgba(80,70,60,.6),rgba(20,15,10,.9))", "linear-gradient(160deg,rgba(230,150,90,.5),rgba(110,40,15,.8))"],
  Oud: ["linear-gradient(160deg,rgba(210,170,255,.5),rgba(90,40,140,.75))", "linear-gradient(160deg,rgba(190,140,80,.5),rgba(60,30,10,.85))"],
  Floral: ["linear-gradient(160deg,rgba(255,200,215,.55),rgba(170,70,100,.7))", "linear-gradient(160deg,rgba(250,235,200,.55),rgba(170,120,60,.7))"],
};
const fallback = palettes.Sweet!;

function hash(text: string): number {
  let h = 0;
  for (let i = 0; i < text.length; i++) {
    h = (h * 31 + text.charCodeAt(i)) | 0;
  }

  return Math.abs(h);
}

export function bottleGlass(mood: string | null | undefined, slug: string): string {
  const options = (mood ? palettes[mood] : undefined) ?? fallback;
  return options[hash(slug) % options.length]!;
}
