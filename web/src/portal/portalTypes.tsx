import React, { useState } from 'react';
import { Button, Card, Chip, Drawer, Surface, Tooltip } from '@heroui/react';
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
  const palette: Record<string, { text: string; iconBg: string; glow: string }> = {
    emerald: {
      text: 'text-emerald-300',
      iconBg: 'bg-emerald-500/15 text-emerald-400 border-emerald-500/30',
      glow: 'from-emerald-500/10'
    },
    sky: {
      text: 'text-sky-300',
      iconBg: 'bg-sky-500/15 text-sky-400 border-sky-500/30',
      glow: 'from-sky-500/10'
    },
    indigo: {
      text: 'text-indigo-300',
      iconBg: 'bg-indigo-500/15 text-indigo-400 border-indigo-500/30',
      glow: 'from-indigo-500/10'
    },
    purple: {
      text: 'text-purple-300',
      iconBg: 'bg-purple-500/15 text-purple-400 border-purple-500/30',
      glow: 'from-purple-500/10'
    },
    amber: {
      text: 'text-amber-300',
      iconBg: 'bg-amber-500/15 text-amber-400 border-amber-500/30',
      glow: 'from-amber-500/12'
    },
    rose: {
      text: 'text-rose-300',
      iconBg: 'bg-rose-500/15 text-rose-400 border-rose-500/30',
      glow: 'from-rose-500/10'
    }
  };
  const style = palette[accent] || palette.emerald;

  return (
    <Card
      variant="default"
      className={`relative overflow-hidden rounded-2xl p-3.5 sm:p-4 bg-linear-to-br ${style.glow} via-surface/90 to-surface/90 transition hover:border-border-secondary`}
    >
      <Card.Header className="p-0 flex-row items-start justify-between gap-2">
        <Card.Description className="text-[11px] sm:text-xs font-medium text-muted">
          {title}
        </Card.Description>
        {icon && (
          <div className={`w-7 h-7 rounded-xl border flex items-center justify-center shrink-0 ${style.iconBg}`}>
            {icon}
          </div>
        )}
      </Card.Header>
      <Card.Content className="p-0 mt-1.5 space-y-1">
        <div className={`text-base sm:text-xl font-bold font-mono tracking-tight truncate ${style.text}`}>
          {value}
        </div>
        <div className="text-[11px] text-muted truncate">{sub}</div>
      </Card.Content>
    </Card>
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
    <span onClick={(e) => e.stopPropagation()} className="inline-flex">
      <Tooltip delay={250}>
        <Button
          size="sm"
          variant={ok ? 'secondary' : 'ghost'}
          isIconOnly={!label}
          aria-label={label || `复制 ${text}`}
          className="h-7 min-w-0 px-2 text-xs text-muted data-[hovered=true]:text-foreground"
          onPress={() => {
            navigator.clipboard?.writeText(text);
            setOk(true);
            onCopied?.();
            setTimeout(() => setOk(false), 1400);
          }}
        >
          {ok ? <Check className="w-3.5 h-3.5 text-emerald-400" /> : <Copy className="w-3.5 h-3.5" />}
          {label && <span className="text-[11px]">{ok ? '已复制' : label}</span>}
        </Button>
        <Tooltip.Content>
          <p className="text-xs font-mono">点击复制: {text}</p>
        </Tooltip.Content>
      </Tooltip>
    </span>
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
    <Surface variant="secondary" className="rounded-xl p-2.5 space-y-1.5 border border-border/60">
      <div className="flex items-center justify-between text-[11px]">
        <div className="flex items-center gap-1.5">
          <span className="w-2 h-2 rounded-full bg-emerald-400 shadow-[0_0_8px_rgba(52,211,153,0.7)]" />
          <span className="text-muted">{isTag ? 'A 队' : '先席'}</span>
          <span className="font-mono font-bold text-emerald-300 text-xs">{team0Lp}</span>
        </div>
        <Chip size="sm" variant="soft" color={turnCount > 0 ? 'accent' : 'default'} className="h-5 px-2 text-[10px] font-mono">
          {turnCount > 0 ? `Turn ${turnCount}` : '待开局'}
        </Chip>
        <div className="flex items-center gap-1.5">
          <span className="font-mono font-bold text-sky-300 text-xs">{team1Lp}</span>
          <span className="text-muted">{isTag ? 'B 队' : '后席'}</span>
          <span className="w-2 h-2 rounded-full bg-sky-400 shadow-[0_0_8px_rgba(56,189,248,0.7)]" />
        </div>
      </div>

      <div className="grid grid-cols-2 gap-1.5 h-1.5">
        <div className="bg-slate-800/90 rounded-full overflow-hidden flex justify-end">
          <div
            className="h-full rounded-full bg-gradient-to-r from-emerald-600 to-emerald-400 transition-all duration-300 motion-reduce:transition-none"
            style={{ width: `${pct0}%` }}
          />
        </div>
        <div className="bg-slate-800/90 rounded-full overflow-hidden">
          <div
            className="h-full rounded-full bg-gradient-to-r from-sky-400 to-sky-600 transition-all duration-300 motion-reduce:transition-none"
            style={{ width: `${pct1}%` }}
          />
        </div>
      </div>
    </Surface>
  );
}

/**
 * 手机端底部弹出抽屉（基于 HeroUI v3 Drawer + React Aria 无障碍焦点控制）。
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
  return (
    <Drawer.Backdrop
      isOpen={open}
      onOpenChange={(nextOpen) => {
        if (!nextOpen) onClose();
      }}
      className="lg:hidden"
    >
      <Drawer.Content placement="bottom" className="lg:hidden">
        <Drawer.Dialog className="max-h-[86vh] rounded-t-3xl bg-overlay text-overlay-foreground border-t border-border pb-safe">
          <Drawer.Handle />
          <Drawer.CloseTrigger aria-label="关闭抽屉" />
          <Drawer.Header className="pb-2 border-b border-separator">
            <Drawer.Heading className="text-sm font-bold truncate">{title}</Drawer.Heading>
            {subtitle && <p className="text-[11px] text-muted truncate mt-0.5">{subtitle}</p>}
          </Drawer.Header>
          <Drawer.Body className="p-4 space-y-3">{children}</Drawer.Body>
        </Drawer.Dialog>
      </Drawer.Content>
    </Drawer.Backdrop>
  );
}
