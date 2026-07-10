export const productVersion = import.meta.env.VITE_FRAMESCOPE_VERSION;

if (!/^\d+\.\d+\.\d+$/.test(productVersion)) {
  throw new Error("VITE_FRAMESCOPE_VERSION is invalid");
}
