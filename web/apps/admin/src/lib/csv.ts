/** Courier files: ";" (our format), "," or tab separated, optional UTF-8 BOM, first row = header. */
export interface CsvPreview {
  delimiter: string;
  header: string[];
  rows: string[][];
  total: number;
}

export function previewCsv(text: string, maxRows = 10): CsvPreview {
  const lines = text.replace(/^\uFEFF/, "").split(/\r?\n/).filter((line) => line.trim() !== "");
  const first = lines[0] ?? "";
  const delimiter = [";", "\t", ","].map((d) => ({ d, count: first.split(d).length })).sort((a, b) => b.count - a.count)[0]!.d;
  const split = (line: string) => line.split(delimiter).map((cell) => cell.trim().replace(/^"(.*)"$/, "$1"));
  return { delimiter, header: split(first), rows: lines.slice(1, 1 + maxRows).map(split), total: Math.max(0, lines.length - 1) };
}
