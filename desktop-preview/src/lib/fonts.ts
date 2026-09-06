/**
 * 字体共享辅助
 *
 * 抽离自 font-context，供主窗口与桌面歌词窗口复用：
 * - registerFontFace：用 FontFace API 注册一个字体
 * - ensureCustomFont：确保某个自定义字体已注册（跨窗口共享 store）
 */

import { store } from "@/lib/store";

/** 自定义字体记录（持久化，与 font-context 保持一致） */
interface CustomFontRecord {
  /** CSS font-family 名称 */
  value: string;
  /** 显示名称 */
  label: string;
  /** base64 编码的字体数据 */
  data: string;
  /** mime 类型 */
  format: string;
}

/** 用 FontFace API 注册一个字体 */
export async function registerFontFace(name: string, base64Data: string, format: string) {
  try {
    const fontFace = new FontFace(
      name,
      `url(data:font/${format === "opentype" ? "opentype" : format};base64,${base64Data})`
    );
    await fontFace.load();
    document.fonts.add(fontFace);
  } catch (error) {
    console.error(`Failed to register font "${name}":`, error);
  }
}

/** 当前 document 是否已注册指定 font-family */
function isFontFaceRegistered(value: string): boolean {
  const existing = Array.from(document.fonts);
  return existing.some(
    (ff) => ff.family === `"${value}"` || ff.family === value
  );
}

/**
 * 确保某个自定义字体已注册到当前窗口的 document.fonts。
 *
 * 桌面歌词是独立窗口，运行期间在主窗口新导入的字体不会自动同步到这里，
 * 需要按需从共享 store（app-custom-fonts）读取并注册。
 */
export async function ensureCustomFont(value: string): Promise<void> {
  if (!value) return;
  if (isFontFaceRegistered(value)) return;
  try {
    const customFonts = await store.get<CustomFontRecord[]>("app-custom-fonts");
    if (!Array.isArray(customFonts)) return;
    const record = customFonts.find((cf) => cf.value === value);
    if (!record) return;
    await registerFontFace(record.value, record.data, record.format);
  } catch (error) {
    console.error(`Failed to ensure font "${value}":`, error);
  }
}