let context: AudioContext | null = null;

/** Two short tones (no audio file). Browsers allow sound only after the user clicked something on the page. */
export function beep() {
  try {
    context ??= new AudioContext();
    const now = context.currentTime;
    [880, 1320].forEach((frequency, index) => {
      const oscillator = context!.createOscillator();
      const gain = context!.createGain();
      oscillator.frequency.value = frequency;
      gain.gain.setValueAtTime(0.15, now + index * 0.18);
      gain.gain.exponentialRampToValueAtTime(0.001, now + index * 0.18 + 0.16);
      oscillator.connect(gain).connect(context!.destination);
      oscillator.start(now + index * 0.18);
      oscillator.stop(now + index * 0.18 + 0.17);
    });
  } catch {
    // no audio (e.g. tests, old browsers): the visual notice is enough
  }
}

const soundKey = "perfumeshop.admin.newOrderSound";

/** A UI preference only (no personal data), so it may live in localStorage. */
export const soundPreference = {
  get: () => window.localStorage.getItem(soundKey) === "on",
  set: (on: boolean) => window.localStorage.setItem(soundKey, on ? "on" : "off"),
};
