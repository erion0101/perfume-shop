import { describe, expect, it } from "vitest";
import { formatDate, formatDateTime } from ".";

describe("dates (Kosovo time)", () => {
  it("formats instants in Europe/Belgrade", () => {
    expect(formatDateTime("2026-11-26T21:30:00Z")).toBe("26.11.2026 22:30");
    expect(formatDateTime("2026-07-10T21:30:00Z")).toBe("10.07.2026 23:30"); // summer time
    expect(formatDate("2026-11-26T23:30:00Z")).toBe("27.11.2026");
  });

  it("keeps calendar dates as they are", () => {
    expect(formatDate("2026-11-20")).toBe("20.11.2026");
    expect(formatDate(null)).toBe("—");
  });
});
