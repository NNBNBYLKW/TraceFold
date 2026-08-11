import type { ApiErrorEnvelope } from "./types";


export class ApiError extends Error {
  readonly code: string;
  readonly status: number;
  readonly details: Record<string, unknown>;
  readonly correlationId: string | null;

  constructor(
    message: string,
    options: {
      code: string;
      status: number;
      details?: Record<string, unknown>;
      correlationId?: string | null;
    },
  ) {
    super(message);
    this.name = "ApiError";
    this.code = options.code;
    this.status = options.status;
    this.details = options.details ?? {};
    this.correlationId = options.correlationId ?? null;
  }
}

export interface ApiRequestOptions {
  idempotencyKey?: string;
  signal?: AbortSignal;
}

const configuredBase = import.meta.env.VITE_API_BASE_URL || "/api";

function endpointUrl(endpoint: string): string {
  if (!endpoint.startsWith("/") || endpoint.startsWith("//")) {
    throw new TypeError("API endpoint must be an application-relative path");
  }
  const origin = window.location.origin === "null" ? "http://localhost" : window.location.origin;
  const base = new URL(configuredBase.endsWith("/") ? configuredBase : `${configuredBase}/`, origin);
  const basePath = base.pathname.replace(/\/+$/, "");
  if (endpoint === basePath || endpoint.startsWith(`${basePath}/`)) {
    return new URL(endpoint, base.origin).toString();
  }
  return new URL(endpoint.slice(1), base).toString();
}

async function request<T>(
  method: "GET" | "POST" | "PATCH",
  endpoint: string,
  body?: unknown,
  options: ApiRequestOptions = {},
): Promise<T> {
  const headers = new Headers({ Accept: "application/json" });
  if (body !== undefined) {
    headers.set("Content-Type", "application/json");
  }
  if (options.idempotencyKey) {
    headers.set("Idempotency-Key", options.idempotencyKey);
  }
  const response = await fetch(endpointUrl(endpoint), {
    method,
    headers,
    body: body === undefined ? undefined : JSON.stringify(body),
    signal: options.signal,
  });
  if (!response.ok) {
    const envelope = await readErrorEnvelope(response);
    throw new ApiError(envelope.error.message, {
      code: envelope.error.code,
      status: response.status,
      details: envelope.error.details,
      correlationId: response.headers.get("X-Correlation-ID"),
    });
  }
  if (response.status === 204) {
    return undefined as T;
  }
  return (await response.json()) as T;
}

async function readErrorEnvelope(response: Response): Promise<ApiErrorEnvelope> {
  try {
    const payload = (await response.json()) as Partial<ApiErrorEnvelope>;
    if (
      payload.error &&
      typeof payload.error.code === "string" &&
      typeof payload.error.message === "string"
    ) {
      return {
        error: {
          code: payload.error.code,
          message: payload.error.message,
          details: payload.error.details ?? {},
        },
      };
    }
  } catch {
    // Use the stable fallback below for non-JSON proxy and transport errors.
  }
  return {
    error: {
      code: `HTTP_${response.status}`,
      message: "请求未能完成，请稍后重试。",
      details: {},
    },
  };
}

export const apiClient = {
  url(endpoint: string): string {
    return endpointUrl(endpoint);
  },
  get<T>(endpoint: string, options?: ApiRequestOptions): Promise<T> {
    return request<T>("GET", endpoint, undefined, options);
  },
  post<T>(
    endpoint: string,
    body?: unknown,
    options?: ApiRequestOptions,
  ): Promise<T> {
    return request<T>("POST", endpoint, body, options);
  },
  patch<T>(
    endpoint: string,
    body: unknown,
    options?: ApiRequestOptions,
  ): Promise<T> {
    return request<T>("PATCH", endpoint, body, options);
  },
};
