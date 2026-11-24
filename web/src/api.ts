export interface AuthUser {
  sessionId: string;
  userId: string;
  username: string;
  displayName: string;
  mustChangePassword: boolean;
  csrfToken: string;
  isStreamerOnly: boolean;
  canViewIp: boolean;
  roleCodes: string[];
  permissions: string[];
  allowedMcpRolesToIssue: string[];
}

let currentCsrfToken = '';

export function setCsrfToken(token: string) {
  currentCsrfToken = token || '';
}

export class ApiError extends Error {
  status: number;
  code?: string;
  data?: unknown;

  constructor(message: string, status: number, code?: string, data?: unknown) {
    super(message);
    this.status = status;
    this.code = code;
    this.data = data;
  }
}

export async function apiFetch<T = any>(path: string, init: RequestInit = {}): Promise<T> {
  const headers = new Headers(init.headers || {});
  if (!(init.body instanceof FormData) && !headers.has('Content-Type') && init.body !== undefined) {
    headers.set('Content-Type', 'application/json');
  }
  if (currentCsrfToken && init.method && init.method.toUpperCase() !== 'GET') {
    headers.set('X-CSRF-Token', currentCsrfToken);
  }

  const res = await fetch(path, {
    ...init,
    headers,
    credentials: 'same-origin'
  });

  const contentType = res.headers.get('content-type') || '';
  const payload = contentType.includes('application/json') ? await res.json() : await res.text();

  if (!res.ok) {
    const msg =
      typeof payload === 'object' && payload !== null && 'message' in payload
        ? String((payload as any).message)
        : `请求失败 (${res.status})`;
    const errCode =
      typeof payload === 'object' && payload !== null && 'error' in payload
        ? String((payload as any).error)
        : undefined;
    throw new ApiError(msg, res.status, errCode, payload);
  }

  return payload as T;
}

export function hasPerm(user: AuthUser | null, perm: string): boolean {
  if (!user) return false;
  if (user.permissions.includes('*')) return true;
  return user.permissions.includes(perm);
}

export async function openAuthenticatedWebSocket(onEvent: (evt: { type: string; timestampUtc: string; data: any }) => void): Promise<WebSocket> {
  const { ticket } = await apiFetch<{ ticket: string }>('/api/v1/auth/ws-ticket', { method: 'POST', body: '{}' });
  const proto = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
  const ws = new WebSocket(`${proto}//${window.location.host}/ws/events?ticket=${encodeURIComponent(ticket)}`);
  ws.onmessage = (ev) => {
    try {
      const parsed = JSON.parse(ev.data);
      onEvent(parsed);
    } catch {
      // ignore malformed frame
    }
  };
  return ws;
}
