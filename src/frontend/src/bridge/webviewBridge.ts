import { createMockBridgeAdapter } from "../data/mockPreview";
import type {
  BridgeError,
  BridgeEventEnvelope,
  BridgeEventType,
  BridgeRequestEnvelope,
  BridgeRequestOptions,
  BridgeRequestType,
  BridgeResponseEnvelope,
  FrameScopeBridgeAdapter,
} from "./contract";

type WebViewMessageHandler = (event: { data: unknown }) => void;

interface ChromeWebView {
  postMessage(message: unknown): void;
  addEventListener(type: "message", listener: WebViewMessageHandler): void;
  removeEventListener?(type: "message", listener: WebViewMessageHandler): void;
}

declare global {
  interface Window {
    chrome?: {
      webview?: ChromeWebView;
    };
  }
}

interface PendingRequest {
  resolve: (payload: unknown) => void;
  reject: (error: BridgeRequestError) => void;
  timeoutId: ReturnType<typeof setTimeout>;
  type: BridgeRequestType;
}

const defaultTimeoutMs = 10000;
let requestCounter = 0;

export class BridgeRequestError extends Error {
  readonly code: string;
  readonly requestId: string;

  constructor(error: BridgeError, requestId = "") {
    super(error.message);
    this.name = "BridgeRequestError";
    this.code = error.code;
    this.requestId = requestId;
  }
}

export class WebViewBridgeClient implements FrameScopeBridgeAdapter {
  readonly environment = "webview2" as const;

  private readonly webview: ChromeWebView;
  private readonly pending = new Map<string, PendingRequest>();
  private readonly listeners = new Map<BridgeEventType, Set<(event: BridgeEventEnvelope) => void>>();
  private readonly onMessage: WebViewMessageHandler;

  constructor(webview = getChromeWebView()) {
    if (!webview) {
      throw new BridgeRequestError({
        code: "webview_unavailable",
        message: "window.chrome.webview is not available.",
      });
    }

    this.webview = webview;
    this.onMessage = (event) => this.handleMessage(event.data);
    this.webview.addEventListener("message", this.onMessage);
  }

  request<TPayload>(
    type: BridgeRequestType,
    payload: Record<string, unknown> = {},
    options: BridgeRequestOptions = {},
  ): Promise<TPayload> {
    const requestId = createRequestId();
    const timeoutMs = Math.max(1, options.timeoutMs ?? defaultTimeoutMs);
    const envelope: BridgeRequestEnvelope = { requestId, type, payload };

    return new Promise<TPayload>((resolve, reject) => {
      const timeoutId = setTimeout(() => {
        this.pending.delete(requestId);
        reject(
          new BridgeRequestError(
            {
              code: "request_timeout",
              message: `${type} timed out after ${timeoutMs} ms.`,
            },
            requestId,
          ),
        );
      }, timeoutMs);

      this.pending.set(requestId, {
        resolve: (responsePayload) => resolve(responsePayload as TPayload),
        reject,
        timeoutId,
        type,
      });

      try {
        this.webview.postMessage(envelope);
      } catch (error) {
        clearTimeout(timeoutId);
        this.pending.delete(requestId);
        reject(
          new BridgeRequestError(
            {
              code: "post_message_failed",
              message: error instanceof Error ? error.message : "Failed to post WebView2 bridge message.",
            },
            requestId,
          ),
        );
      }
    });
  }

  subscribe<TPayload>(
    type: BridgeEventType,
    listener: (event: BridgeEventEnvelope<TPayload>) => void,
  ): () => void {
    const typedListener = listener as (event: BridgeEventEnvelope) => void;
    const bucket = this.listeners.get(type) ?? new Set<(event: BridgeEventEnvelope) => void>();
    bucket.add(typedListener);
    this.listeners.set(type, bucket);

    return () => {
      const current = this.listeners.get(type);
      current?.delete(typedListener);
      if (current?.size === 0) this.listeners.delete(type);
    };
  }

  dispose() {
    this.webview.removeEventListener?.("message", this.onMessage);
    for (const [requestId, pending] of this.pending) {
      clearTimeout(pending.timeoutId);
      pending.reject(
        new BridgeRequestError(
          {
            code: "bridge_disposed",
            message: `${pending.type} was cancelled because the bridge was disposed.`,
          },
          requestId,
        ),
      );
    }
    this.pending.clear();
    this.listeners.clear();
  }

  notifyReady(payload: Record<string, unknown> = {}) {
    this.webview.postMessage({
      type: "webview-ready",
      payload: {
        client: "react",
        ...payload,
      },
    });
  }

  private handleMessage(rawMessage: unknown) {
    const message = normalizeIncomingMessage(rawMessage);
    if (!message || typeof message.type !== "string") return;

    if (message.type === "response") {
      this.handleResponse(message as unknown as BridgeResponseEnvelope);
      return;
    }

    if (message.type.startsWith("event.")) {
      this.handleEvent(message as unknown as BridgeEventEnvelope);
    }
  }

  private handleResponse(response: BridgeResponseEnvelope) {
    const pending = this.pending.get(response.requestId);
    if (!pending) return;

    clearTimeout(pending.timeoutId);
    this.pending.delete(response.requestId);

    if (response.ok) {
      pending.resolve(response.payload);
      return;
    }

    pending.reject(
      new BridgeRequestError(
        response.error ?? {
          code: "bridge_error",
          message: "Bridge request failed.",
        },
        response.requestId,
      ),
    );
  }

  private handleEvent(event: BridgeEventEnvelope) {
    const listeners = this.listeners.get(event.type);
    if (!listeners) return;
    for (const listener of listeners) listener(event);
  }
}

export function isWebView2BridgeAvailable() {
  return Boolean(getChromeWebView());
}

export function createFrameScopeBridgeAdapter(): FrameScopeBridgeAdapter {
  const webview = getChromeWebView();
  if (!webview) return createMockBridgeAdapter();
  const client = new WebViewBridgeClient(webview);
  client.notifyReady();
  return client;
}

function getChromeWebView(): ChromeWebView | undefined {
  return typeof window !== "undefined" ? window.chrome?.webview : undefined;
}

function createRequestId() {
  requestCounter += 1;
  return `fs-${Date.now().toString(36)}-${requestCounter.toString(36)}`;
}

function normalizeIncomingMessage(rawMessage: unknown): Record<string, unknown> | null {
  if (!rawMessage) return null;
  if (typeof rawMessage === "string") {
    try {
      return JSON.parse(rawMessage) as Record<string, unknown>;
    } catch {
      return null;
    }
  }
  if (typeof rawMessage === "object") return rawMessage as Record<string, unknown>;
  return null;
}
