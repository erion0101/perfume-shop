import { createHmac } from "node:crypto";

/** RFC 6238 code (SHA-1, 30 s, 6 digits) for an authenticator key as the admin shows it ("abcd efgh …", base32). */
export function totp(key: string, at = Date.now()): string {
  const alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
  let bits = "";
  for (const char of key.replace(/[\s=-]/g, "").toUpperCase()) {
    const value = alphabet.indexOf(char);
    if (value < 0) {
      throw new Error(`Not base32: ${char}`);
    }

    bits += value.toString(2).padStart(5, "0");
  }

  const secret = Buffer.from(bits.match(/.{8}/g)!.map((byte) => parseInt(byte, 2)));
  const counter = Buffer.alloc(8);
  counter.writeBigUInt64BE(BigInt(Math.floor(at / 1000 / 30)));
  const hmac = createHmac("sha1", secret).update(counter).digest();
  const offset = hmac[hmac.length - 1]! & 0xf;
  return String((hmac.readUInt32BE(offset) & 0x7fffffff) % 1_000_000).padStart(6, "0");
}
