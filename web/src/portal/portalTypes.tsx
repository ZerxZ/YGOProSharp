import React, { useEffect, useState } from 'react';
import { Check, Copy, X } from 'lucide-react';

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

export function StatBox({
  title,
  value,
  sub,
  icon,
  accent = 'emerald'
}: {
  title: string;
  value: string;
  sub: string;
  icon?: React.ReactNode;
  accent?: 'emerald' | 'sky' | 'indigo' | 'purple' | 'amber' | 'rose';
}) {
  const palette: Record<string, { text: string; iconBg: string }> = {
    emerald: { text: 'text-emerald-300', iconBg: 'bg-emerald-500/15 text-emerald-400 border-emerald-500/25' },
    sky: { text: 'text-sky-300', iconBg: 'bg-sky-500/15 text-sky-400 border-sky-500/25' },
    indigo: { text: 'text-indigo-300', iconBg: 'bg-indigo-500/15 text-indigo-400 border-indigo-500/25' },
    purple: { text: 'text-purple-300', iconBg: 'bg-purple-500/15 text-purple-400 border-purple-500/25' },
    amber: { text: 'text-amber-300', iconBg: 'bg-amber-500/15 text-amber-400 border-amber-500/25' },
    rose: { text: 'text-rose-300', iconBg: 'bg-rose-500/15 text-rose-400 border-rose-500/25' }
  };
  const style = palette[accent] || palette.emerald;

  return (
    <div className="glass-card rounded-2xl p-3.5 sm:p-4 transition hover:border-slate-700/80">
      <div className="flex items-start justify-between gap-2">
        <div className="text-[11px] sm:text-xs font-medium text-slate-400">{title}</div>
        {icon && (
          <div className={`w-7 h-7 rounded-lg border flex items-center justify-center shrink-0 ${style.iconBg}`}>
            {icon}
          </div>
        )}
      </div>
      <div className={`text-base sm:text-xl font-bold font-mono tracking-tight mt-1.5 truncate ${style.text}`}>
        {value}
      </div>
      <div className="text-[11px] text-slate-400/90 mt-1 truncate">{sub}</div>
    </div>
  );
}

export function SmallCopy({
  text,
  label,
  onCopied
}: {
  text: string;
  label?: string;
  onCopied?: () => void;
}) {
  const [ok, setOk] = useState(false);
  return (
    <button
      type="button"
      onClick={(e) => {
        e.stopPropagation();
        navigator.clipboard?.writeText(text);
        setOk(true);
        onCopied?.();
        setTimeout(() => setOk(false), 1400);
      }}
      className="inline-flex items-center gap-1 p-1.5 rounded-lg hover:bg-slate-800/90 active:scale-95 text-slate-400 hover:text-white transition cursor-pointer"
      title={`复制: ${text}`}
    >
      {ok ? <Check className="w-3.5 h-3.5 text-emerald-400" /> : <Copy className="w-3.5 h-3.5" />}
      {label && <span className="text-[11px]">{ok ? '已复制' : label}</span>}
    </button>
  );
}

/**
 * 直观的双阵营基本分 (LP) 对抗血条（自动兼容 8000 / 16000 / 自定义初始 LP）。
 */
export function LpVersusBar({
  team0Lp,
  team1Lp,
  startLp = 8000,
  turnCount = 0,
  isTag = false
}: {
  team0Lp: number;
  team1Lp: number;
  startLp?: number;
  turnCount?: number;
  isTag?: boolean;
}) {
  const base = Math.max(1, startLp || (isTag ? 16000 : 8000), team0Lp, team1Lp);
  const pct0 = Math.max(4, Math.min(100, Math.round((Math.max(0, team0Lp) / base) * 100)));
  const pct1 = Math.max(4, Math.min(100, Math.round((Math.max(0, team1Lp) / base) * 100)));

  return (
    <div className="glass-subcard rounded-xl p-2.5 space-y-1.5">
      <div className="flex items-center justify-between text-[11px]">
        <div className="flex items-center gap-1.5">
          <span className="w-2 h-2 rounded-full bg-emerald-400 shadow-[0_0_8px_rgba(52,211,153,0.7)]" />
          <span className="text-slate-400">{isTag ? 'A 队' : '先席'}</span>
          <span className="font-mono font-bold text-emerald-300 text-xs">{team0Lp}</span>
        </div>
        <span className="px-1.5 py-0.5 rounded-full bg-slate-900 border border-slate-800 font-mono text-[10px] text-slate-400">
          {turnCount > 0 ? `Turn ${turnCount}` : '待开局'}
        </span>
        <div className="flex items-center gap-1.5">
          <span className="font-mono font-bold text-sky-300 text-xs">{team1Lp}</span>
          <span className="text-slate-400">{isTag ? 'B 队' : '后席'}</span>
          <span className="w-2 h-2 rounded-full bg-sky-400 shadow-[0_0_8px_rgba(56,189,248,0.7)]" />
        </div>
      </div>

      <div className="grid grid-cols-2 gap-1.5 h-1.5">
        <div className="bg-slate-800/90 rounded-full overflow-hidden flex justify-end">
          <div
            className="h-full rounded-full bg-gradient-to-r from-emerald-600 to-emerald-400 transition-all duration-300"
            style={{ width: `${pct0}%` }}
          />
        </div>
        <div className="bg-slate-800/90 rounded-full overflow-hidden">
          <div
            className="h-full rounded-full bg-gradient-to-r from-sky-400 to-sky-600 transition-all duration-300"
            style={{ width: `${pct1}%` }}
          />
        </div>
      </div>
    </div>
  );
}

/**
 * 手机端底部弹出抽屉（Bottom Sheet）：解决移动端点击房间/详情后内容远在页面底部看不见的问题。
 */
export function MobileBottomSheet({
  open,
  title,
  subtitle,
  onClose,
  children
}: {
  open: boolean;
  title: string;
  subtitle?: string;
  onClose: () => void;
  children: React.ReactNode;
}) {
  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose();
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [open, onClose]);

  if (!open) return null;

  return (
    <div
      className="lg:hidden fixed inset-0 z-50 bg-black/70 backdrop-blur-xs flex items-end justify-center"
      onClick={onClose}
    >
      <div
        onClick={(e) => e.stopPropagation()}
        className="w-full max-h-[86vh] bg-slate-900 border-t border-slate-700/80 rounded-t-3xl shadow-2xl flex flex-col animate-slide-up pb-safe"
      >
        <div className="pt-2.5 pb-1 flex justify-center">
          <div className="w-10 h-1 rounded-full bg-slate-700" />
        </div>
        <div className="px-4 py-2.5 border-b border-slate-800 flex items-center justify-between gap-3">
          <div className="min-w-0">
            <div className="font-bold text-sm text-slate-100 truncate">{title}</div>
            {subtitle && <div className="text-[11px] text-slate-400 truncate">{subtitle}</div>}
          </div>
          <button
            type="button"
            onClick={onClose}
            className="p-2 rounded-xl bg-slate-800/90 hover:bg-slate-700 text-slate-300 shrink-0 cursor-pointer"
            aria-label="关闭抽屉"
          >
            <X className="w-4 h-4" />
          </button>
        </div>
        <div className="p-4 overflow-y-auto space-y-3 flex-1">{children}</div>
      </div>
    </div>
  );
}
