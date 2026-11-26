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

let adminCsrfToken = '';
let portalCsrfToken = '';

export function setCsrfToken(token: string) {
  adminCsrfToken = token || '';
}

export function setPortalCsrfToken(token: string) {
  portalCsrfToken = token || '';
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
  const activeCsrf = path.startsWith('/api/portal') ? portalCsrfToken : adminCsrfToken;
  if (activeCsrf && init.method && init.method.toUpperCase() !== 'GET') {
    headers.set('X-CSRF-Token', activeCsrf);
  }

  let res: Response;
  try {
    res = await fetch(path, {
      ...init,
      headers,
      credentials: 'same-origin'
    });
  } catch (networkErr) {
    throw new ApiError(
      '网络连接中断或服务端正在重启（若刚更新代码请重启 YGOProSharp.Cli 后重试）',
      0,
      'network_error',
      networkErr
    );
  }

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

export interface UploadProgressInfo {
  loaded: number;
  total: number;
  percent: number;
}

export function apiUploadWithProgress<T = any>(
  path: string,
  formData: FormData,
  onProgress?: (info: UploadProgressInfo) => void
): Promise<T> {
  return new Promise<T>((resolve, reject) => {
    const xhr = new XMLHttpRequest();
    xhr.open('POST', path, true);
    xhr.withCredentials = true;

    const activeCsrf = path.startsWith('/api/portal') ? portalCsrfToken : adminCsrfToken;
    if (activeCsrf) {
      xhr.setRequestHeader('X-CSRF-Token', activeCsrf);
    }

    xhr.upload.onprogress = (ev) => {
      if (ev.lengthComputable && onProgress) {
        const percent = ev.total > 0 ? Math.min(100, Math.round((ev.loaded / ev.total) * 100)) : 0;
        onProgress({ loaded: ev.loaded, total: ev.total, percent });
      }
    };

    xhr.onerror = () => {
      reject(
        new ApiError(
          '上传连接被中断：请确认服务端在线且已重启应用最新大文件配置（上限 256MB）',
          0,
          'upload_network_error'
        )
      );
    };

    xhr.onload = () => {
      const contentType = xhr.getResponseHeader('content-type') || '';
      let payload: any = xhr.responseText;
      if (contentType.includes('application/json')) {
        try {
          payload = JSON.parse(xhr.responseText);
        } catch {
          // keep text
        }
      }

      if (xhr.status >= 200 && xhr.status < 300) {
        resolve(payload as T);
      } else {
        const msg =
          typeof payload === 'object' && payload !== null && 'message' in payload
            ? String(payload.message)
            : `上传失败 (HTTP ${xhr.status})`;
        reject(new ApiError(msg, xhr.status, payload?.error, payload));
      }
    };

    xhr.send(formData);
  });
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
