import { invoke } from "@tauri-apps/api/core";

/**
 * UAPI (uapis.cn) 前端封装。
 *
 * 当前支持「随机图片」接口：
 *   GET https://uapis.cn/api/v1/random/image
 *
 * 调用链：前端 → Tauri 命令 `get_random_image`（Rust 后端发起 HTTP 请求，
 * 携带 Authorization: Bearer <KEY>，规避 WebView 跨域限制）→ base64。
 */

/** 接口文档支持的主类别（与官方文档一致） */
export const RANDOM_IMAGE_CATEGORIES = [
  "acg",
  "landscape",
  "anime",
  "pc_wallpaper",
  "mobile_wallpaper",
  "general_anime",
  "ai_drawing",
  "bq",
  "furry",
] as const;

/** 主类别中文标注（用于下拉显示，接口参数仍用英文原值） */
export const RANDOM_IMAGE_CATEGORY_LABELS: Record<string, string> = {
  acg: "二次元动漫",
  landscape: "风景图",
  anime: "混合动漫",
  pc_wallpaper: "电脑壁纸",
  mobile_wallpaper: "手机壁纸",
  general_anime: "动漫图",
  ai_drawing: "AI绘画",
  bq: "表情包/趣图",
  furry: "福瑞",
};

/** 接口文档支持的子类别（与官方文档一致） */
export const RANDOM_IMAGE_TYPES = [
  "pc",
  "mb",
  "eciyuan",
  "ikun",
  "4k",
  "s4k",
  "z4k",
  "szs8k",
  "xiongmao",
  "maomao",
  "waiguoren",
] as const;

/** 子类别中文标注（用于下拉显示，接口参数仍用英文原值） */
export const RANDOM_IMAGE_TYPE_LABELS: Record<string, string> = {
  pc: "电脑",
  mb: "手机",
  eciyuan: "二次元",
  ikun: "iKun",
  "4k": "4K",
  s4k: "横屏4K",
  z4k: "竖屏4K",
  szs8k: "竖屏8K",
  xiongmao: "熊猫头",
  maomao: "猫猫",
  waiguoren: "外国人",
};

/** 各主类别支持的子类别（文档：type 仅 UapiPro 服务器图片支持，外部图床与 anime 混合类别不支持） */
export const RANDOM_IMAGE_TYPE_MAP: Record<string, readonly string[]> = {
  acg: ["pc", "mb"],
  bq: ["xiongmao", "waiguoren", "maomao", "ikun", "eciyuan"],
  furry: ["z4k", "szs8k", "s4k", "4k"],
};

/** 不支持 type 参数的主类别（外部图床 / anime 混合类别） */
export const RANDOM_IMAGE_TYPELESS_CATEGORIES: readonly string[] = [
  "landscape",
  "anime",
  "pc_wallpaper",
  "mobile_wallpaper",
  "general_anime",
  "ai_drawing",
];

export type RandomImageCategory = (typeof RANDOM_IMAGE_CATEGORIES)[number];
export type RandomImageType = (typeof RANDOM_IMAGE_TYPES)[number];

/**
 * 解析后端返回的错误字符串（格式 `CODE|message`），映射为可直接展示的提示。
 * 未知 CODE 时回退展示原始 message。
 */
export function parseUapiError(raw: string): string {
  const code = raw.split("|")[0] ?? "";
  const message = raw.includes("|") ? raw.split("|").slice(1).join("|") : raw;

  switch (code) {
    case "NOT_FOUND":
      return "未找到该类别的图片，请换个分类或稍后再试";
    case "INTERNAL_SERVER_ERROR":
      return "服务器内部错误，请稍后重试";
    case "INVALID_CATEGORY":
      return message;
    case "INVALID_TYPE":
      return message;
    case "NETWORK_ERROR":
      return "网络请求失败，请检查网络连接";
    case "READ_ERROR":
      return "读取图片数据失败，请重试";
    case "CLIENT_ERROR":
      return message;
    case "EMPTY_RESPONSE":
      return "接口返回了空内容，请重试";
    case "EMPTY_DATA":
      return "图片数据为空，无法保存";
    case "INVALID_PATH":
      return message;
    case "DECODE_ERROR":
      return message;
    case "WRITE_ERROR":
      return message;
    case "401":
      return "API 密钥无效或已过期，请在设置中检查";
    case "402":
      return "积分或余额不足，无法获取图片";
    case "403":
      return "访问被拒绝，请检查 API 密钥与接口权限";
    case "429":
      return "请求过于频繁，请稍后再试";
    default:
      return message || `请求失败（${code}）`;
  }
}

/**
 * 获取一张随机图片，返回图片二进制的 base64 字符串。
 *
 * @param category 可选主类别（acg / landscape / anime / ...），不传则全局随机
 * @param type 可选子类别（pc / mb / z4k / ...），仅部分主类别支持
 * @returns base64 字符串（image/jpeg）
 *
 * 密钥来源：由后端从环境变量 UAPI_API_KEY 读取（设置页已不提供密钥配置）。
 */
export async function fetchRandomImageRaw(
  category?: RandomImageCategory | string,
  type?: RandomImageType | string
): Promise<string> {
  let base64: string;
  try {
    base64 = await invoke<string>("get_random_image", {
      category: category ?? null,
      imageType: type ?? null,
      apiKey: null,
    });
  } catch (e) {
    // Tauri 命令 Rejected 时 e 就是后端返回的错误字符串
    const raw = typeof e === "string" ? e : e instanceof Error ? e.message : String(e);
    throw new Error(parseUapiError(raw));
  }

  return base64;
}

/**
 * 获取一张随机图片，返回可直接用于 <img> 的 Blob。
 */
export async function fetchRandomImage(
  category?: RandomImageCategory | string,
  type?: RandomImageType | string
): Promise<Blob> {
  return base64ToBlob(await fetchRandomImageRaw(category, type));
}

/**
 * 将随机图片的 base64 数据保存到指定路径（配合 `save()` 对话框使用）。
 */
export async function saveRandomImage(base64: string, path: string): Promise<void> {
  try {
    await invoke("save_random_image_bytes", { base64Data: base64, path });
  } catch (e) {
    const raw = typeof e === "string" ? e : e instanceof Error ? e.message : String(e);
    throw new Error(parseUapiError(raw));
  }
}

/** 将 base64 字符串解码为 Blob（默认按 JPEG 处理） */
export function base64ToBlob(base64: string, mimeType = "image/jpeg"): Blob {
  const binary = atob(base64);
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) {
    bytes[i] = binary.charCodeAt(i);
  }
  return new Blob([bytes], { type: mimeType });
}
