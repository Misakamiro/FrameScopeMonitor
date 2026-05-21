import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { BridgeEventEnvelope, StateSnapshotPayload } from "./contract";
import { BridgeRequestError, WebViewBridgeClient } from "./webviewBridge";

type MessageHandler = (event: { data: unknown }) => void;

function installWebViewMock() {
  const handlers = new Set<MessageHandler>();
  const posted: unknown[] = [];
  const webview = {
    postMessage: vi.fn((message: unknown) => {
      posted.push(message);
    }),
    addEventListener: vi.fn((_type: string, handler: MessageHandler) => {
      handlers.add(handler);
    }),
    removeEventListener: vi.fn((_type: string, handler: MessageHandler) => {
      handlers.delete(handler);
    }),
  };

  vi.stubGlobal("window", { chrome: { webview } });

  return {
    webview,
    posted,
    emit(data: unknown) {
      for (const handler of handlers) handler({ data });
    },
  };
}

describe("WebViewBridgeClient", () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  it("posts a requestId and resolves the matching response payload", async () => {
    const bridgeHost = installWebViewMock();
    const client = new WebViewBridgeClient();
    const pending = client.request<StateSnapshotPayload>("state.snapshot", {}, { timeoutMs: 5000 });
    const request = bridgeHost.posted[0] as { requestId: string; type: string; payload: unknown };

    expect(request.type).toBe("state.snapshot");
    expect(request.requestId).toMatch(/^fs-/);
    expect(request.payload).toEqual({});

    bridgeHost.emit({
      requestId: request.requestId,
      type: "response",
      ok: true,
      payload: { bridgeStatus: "ready" },
      error: null,
    });

    await expect(pending).resolves.toEqual({ bridgeStatus: "ready" });
  });

  it("rejects bridge error responses", async () => {
    const bridgeHost = installWebViewMock();
    const client = new WebViewBridgeClient();
    const pending = client.request("config.get", {}, { timeoutMs: 5000 });
    const request = bridgeHost.posted[0] as { requestId: string };

    bridgeHost.emit({
      requestId: request.requestId,
      type: "response",
      ok: false,
      payload: {},
      error: { code: "handler_failed", message: "Config failed" },
    });

    await expect(pending).rejects.toMatchObject({
      code: "handler_failed",
      message: "Config failed",
    });
  });

  it("rejects timed out requests instead of leaving controls loading forever", async () => {
    installWebViewMock();
    const client = new WebViewBridgeClient();
    const pending = client.request("config.get", {}, { timeoutMs: 25 });
    const assertion = expect(pending).rejects.toMatchObject({
      code: "request_timeout",
    });

    await vi.advanceTimersByTimeAsync(26);

    await assertion;
    await pending.catch((error) => {
      expect(error).toBeInstanceOf(BridgeRequestError);
    });
  });

  it("supports event subscribe and unsubscribe", () => {
    const bridgeHost = installWebViewMock();
    const client = new WebViewBridgeClient();
    const listener = vi.fn();
    const unsubscribe = client.subscribe("event.processesRefreshed", listener);
    const event: BridgeEventEnvelope = {
      type: "event.processesRefreshed",
      payload: { requestId: "process-1", count: 0 },
      sentAt: "2026-05-20T00:00:00.000Z",
    };

    bridgeHost.emit(event);
    unsubscribe();
    bridgeHost.emit(event);

    expect(listener).toHaveBeenCalledTimes(1);
    expect(listener).toHaveBeenCalledWith(event);
  });
});
