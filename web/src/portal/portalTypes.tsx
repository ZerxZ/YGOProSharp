import { useState } from 'react';
import { Check, Copy } from 'lucide-react';

export interface SurfaceInfo {
  surface: 'admin' | 'portal';
  unifiedPortal?: boolean;
  defaultPage?: string;
  adminPath?: string;
  gamePort: number;
  adminPort?: number;
  portalPort?: number;
  publicGameHost?: string;
  mcpPortHint?: number;
}

export interface PortalUser {
  userId: string;
  username: string;
  displayName: string;
  mustChangePassword: boolean;
  csrfToken: string;
  roleCodes: string[];
  allowedMcpRolesToIssue: string[];
}

export function StatBox({ title, value, sub }: { title: string; value: string; sub: string }) {
  return (
    <div className="bg-slate-900 border border-slate-800 rounded-xl p-4 space-y-1">
      <div className="text-xs text-slate-400">{title}</div>
      <div className="text-lg font-bold text-emerald-300 font-mono">{value}</div>
      <div className="text-[11px] text-slate-500">{sub}</div>
    </div>
  );
}

export function SmallCopy({ text, onCopied }: { text: string; onCopied?: () => void }) {
  const [ok, setOk] = useState(false);
  return (
    <button
      type="button"
      onClick={(e) => {
        e.stopPropagation();
        navigator.clipboard?.writeText(text);
        setOk(true);
        onCopied?.();
        setTimeout(() => setOk(false), 1200);
      }}
      className="p-1 rounded hover:bg-slate-800 text-slate-400 hover:text-white cursor-pointer"
      title="复制"
    >
      {ok ? <Check className="w-3.5 h-3.5 text-emerald-400" /> : <Copy className="w-3.5 h-3.5" />}
    </button>
  );
}
