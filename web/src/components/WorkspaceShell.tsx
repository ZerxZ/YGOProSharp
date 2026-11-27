import React, { useEffect, useState } from 'react';
import { Button, Drawer, Tooltip } from '@heroui/react';
import {
  ChevronDown,
  ChevronRight,
  ChevronUp,
  Maximize2,
  Minimize2,
  Moon,
  PanelLeft,
  PanelRight,
  Search,
  Sun,
  Terminal,
  X
} from 'lucide-react';

export type UiColorMode = 'dark' | 'light';

export function useUiColorMode(): [UiColorMode, () => void] {
  const [mode, setMode] = useState<UiColorMode>(() => {
    if (typeof window === 'undefined') return 'dark';
    const saved = window.localStorage.getItem('ygo_ui_theme');
    return saved === 'light' ? 'light' : 'dark';
  });

  useEffect(() => {
    const root = document.documentElement;
    root.classList.remove('dark', 'light');
    root.classList.add(mode);
    root.setAttribute('data-theme', mode);
    try {
      window.localStorage.setItem('ygo_ui_theme', mode);
    } catch {
      // ignore storage errors
    }
  }, [mode]);

  const toggle = () => setMode((prev) => (prev === 'dark' ? 'light' : 'dark'));
  return [mode, toggle];
}

export interface WorkspaceNavItem {
  key: string;
  label: string;
  shortLabel: string;
  icon: React.ReactNode;
  badge?: string | number;
}

export interface WorkspaceSidebarGroupItem {
  id: string;
  title: string;
  subtitle?: string;
  meta?: string;
  statusColor?: 'emerald' | 'amber' | 'sky' | 'purple' | 'muted';
  active?: boolean;
  onSelect: () => void;
}

export interface WorkspaceSidebarGroup {
  id: string;
  label: string;
  count?: number;
  defaultOpen?: boolean;
  emptyText?: string;
  items: WorkspaceSidebarGroupItem[];
}

export interface WorkspaceInspectorTab {
  key: string;
  label: string;
  badge?: string | number;
  icon?: React.ReactNode;
  content: React.ReactNode;
}

export interface ConsoleLogEntry {
  id: string;
  time: string;
  level?: 'info' | 'event' | 'chat' | 'warn';
  source: string;
  message: string;
}

export interface WorkspaceShellProps {
  themeClass: 'portal-theme' | 'admin-theme';
  ambientClass?: string;
  monogram?: string;
  monogramClass?: string;
  workspaceName: string;
  workspaceBranchTag?: string;
  breadcrumbCurrent: string;
  statusBadge?: React.ReactNode;
  navItems: WorkspaceNavItem[];
  activeNavKey: string;
  onSelectNav: (key: string) => void;
  sidebarQuickAction?: {
    label: string;
    icon: React.ReactNode;
    onPress: () => void;
  };
  sidebarGroups?: WorkspaceSidebarGroup[];
  sidebarFooterTop?: React.ReactNode;
  sidebarUtilityLeft?: React.ReactNode;
  sidebarUtilityRight?: React.ReactNode;
  headerRightActions?: React.ReactNode;
  inspectorTabs?: WorkspaceInspectorTab[];
  activeInspectorKey?: string;
  onChangeInspectorKey?: (key: string) => void;
  inspectorOpen?: boolean;
  onInspectorOpenChange?: (open: boolean) => void;
  mobileInspectorOpen?: boolean;
  onMobileInspectorOpenChange?: (open: boolean) => void;
  consoleLogs?: ConsoleLogEntry[];
  consoleStatusText?: string;
  toast?: React.ReactNode;
  modals?: React.ReactNode;
  children: React.ReactNode;
}

const DOT_COLORS: Record<NonNullable<WorkspaceSidebarGroupItem['statusColor']>, string> = {
  emerald: 'bg-emerald-400 shadow-[0_0_6px_rgba(52,211,153,0.75)]',
  amber: 'bg-amber-400',
  sky: 'bg-sky-400',
  purple: 'bg-purple-400',
  muted: 'bg-zinc-500'
};

export function WorkspaceShell({
  themeClass,
  ambientClass = '',
  monogram = 'YP',
  monogramClass = 'bg-emerald-500/20 text-emerald-300 border-emerald-500/30',
  workspaceName,
  workspaceBranchTag = 'main',
  breadcrumbCurrent,
  statusBadge,
  navItems,
  activeNavKey,
  onSelectNav,
  sidebarQuickAction,
  sidebarGroups = [],
  sidebarFooterTop,
  sidebarUtilityLeft,
  sidebarUtilityRight,
  headerRightActions,
  inspectorTabs = [],
  activeInspectorKey,
  onChangeInspectorKey,
  inspectorOpen = true,
  onInspectorOpenChange,
  mobileInspectorOpen = false,
  onMobileInspectorOpenChange,
  consoleLogs = [],
  consoleStatusText,
  toast,
  modals,
  children
}: WorkspaceShellProps) {
  const [colorMode, toggleColorMode] = useUiColorMode();
  const [desktopSidebarOpen, setDesktopSidebarOpen] = useState(true);
  const [mobileNavOpen, setMobileNavOpen] = useState(false);
  const [sidebarFilter, setSidebarFilter] = useState('');
  const [inspectorWide, setInspectorWide] = useState(false);
  const [consoleExpanded, setConsoleExpanded] = useState(false);
  const [collapsedGroups, setCollapsedGroups] = useState<Record<string, boolean>>({});

  const hasInspector = inspectorTabs.length > 0;
  const resolvedInspectorKey =
    activeInspectorKey && inspectorTabs.some((t) => t.key === activeInspectorKey)
      ? activeInspectorKey
      : inspectorTabs[0]?.key;
  const currentInspectorTab = inspectorTabs.find((t) => t.key === resolvedInspectorKey) || inspectorTabs[0];

  const toggleGroup = (id: string, defaultOpen = true) => {
    setCollapsedGroups((prev) => {
      const cur = prev[id] ?? !defaultOpen;
      return { ...prev, [id]: !cur };
    });
  };

  const kw = sidebarFilter.trim().toLowerCase();
  const visibleNavItems = kw
    ? navItems.filter(
        (n) => n.label.toLowerCase().includes(kw) || n.shortLabel.toLowerCase().includes(kw)
      )
    : navItems;

  const renderSidebarContent = (isMobileDrawer: boolean) => (
    <div className="flex flex-col h-full bg-[var(--sidebar-bg)] text-foreground select-none">
      {/* 顶部品牌栏 (对齐 T3 Code 左上角) */}
      <div className="h-11 px-3 border-b border-border flex items-center justify-between gap-2 shrink-0">
        <div className="flex items-center gap-2 min-w-0">
          <span
            className={`w-5 h-5 rounded-md border text-[10px] font-extrabold tracking-tight flex items-center justify-center shrink-0 ${monogramClass}`}
          >
            {monogram}
          </span>
          <span className="font-semibold text-[13px] truncate">{workspaceName}</span>
          {workspaceBranchTag && (
            <span className="text-[10px] font-mono px-1.5 py-0.5 rounded bg-surface-secondary text-muted border border-border/70 shrink-0">
              {workspaceBranchTag}
            </span>
          )}
        </div>

        <div className="flex items-center gap-0.5 shrink-0">
          {sidebarQuickAction && (
            <Tooltip delay={200}>
              <Button
                isIconOnly
                size="sm"
                variant="ghost"
                aria-label={sidebarQuickAction.label}
                className="h-7 w-7 min-w-0 text-muted data-[hovered=true]:text-foreground"
                onPress={() => {
                  sidebarQuickAction.onPress();
                  if (isMobileDrawer) setMobileNavOpen(false);
                }}
              >
                {sidebarQuickAction.icon}
              </Button>
              <Tooltip.Content>
                <p className="text-xs">{sidebarQuickAction.label}</p>
              </Tooltip.Content>
            </Tooltip>
          )}

          {isMobileDrawer ? (
            <Button
              isIconOnly
              size="sm"
              variant="ghost"
              aria-label="关闭侧栏"
              className="h-7 w-7 min-w-0 text-muted data-[hovered=true]:text-foreground"
              onPress={() => setMobileNavOpen(false)}
            >
              <X className="w-3.5 h-3.5" />
            </Button>
          ) : (
            <Tooltip delay={200}>
              <Button
                isIconOnly
                size="sm"
                variant="ghost"
                aria-label="收起左侧导航栏"
                className="h-7 w-7 min-w-0 text-muted data-[hovered=true]:text-foreground"
                onPress={() => setDesktopSidebarOpen(false)}
              >
                <PanelLeft className="w-3.5 h-3.5" />
              </Button>
              <Tooltip.Content>
                <p className="text-xs">收起侧边栏</p>
              </Tooltip.Content>
            </Tooltip>
          )}
        </div>
      </div>

      {/* 快速搜索过滤框 */}
      <div className="px-2.5 pt-2.5 pb-1.5 shrink-0">
        <div className="relative flex items-center">
          <Search className="w-3.5 h-3.5 text-muted absolute left-2.5 pointer-events-none" />
          <input
            value={sidebarFilter}
            onChange={(e) => setSidebarFilter(e.target.value)}
            placeholder="搜索模块或活动项..."
            className="w-full h-8 pl-8 pr-7 text-xs rounded-lg bg-surface border border-border/80 text-foreground placeholder:text-muted focus:outline-none focus:border-accent transition"
          />
          {sidebarFilter && (
            <button
              type="button"
              onClick={() => setSidebarFilter('')}
              className="absolute right-2 text-muted hover:text-foreground cursor-pointer"
            >
              <X className="w-3 h-3" />
            </button>
          )}
        </div>
      </div>

      {/* 主导航列表 + 动态分组列表 (如 运行中房间 / 最近战报) */}
      <div className="flex-1 overflow-y-auto px-2 py-1.5 space-y-4">
        <div className="space-y-0.5">
          <div className="px-2 pb-1 text-[10px] font-semibold uppercase tracking-wider text-muted/80">
            工作区视图
          </div>
          {visibleNavItems.map((item) => {
            const active = item.key === activeNavKey;
            return (
              <button
                key={item.key}
                type="button"
                onClick={() => {
                  onSelectNav(item.key);
                  if (isMobileDrawer) setMobileNavOpen(false);
                }}
                className={`w-full h-8 px-2.5 rounded-lg text-[12.5px] flex items-center justify-between gap-2 transition cursor-pointer ${
                  active
                    ? 'bg-surface-secondary text-foreground font-semibold border border-border/80 shadow-2xs'
                    : 'text-muted hover:text-foreground hover:bg-surface/65 border border-transparent'
                }`}
              >
                <span className="flex items-center gap-2 min-w-0 truncate">
                  <span className={active ? 'text-accent shrink-0' : 'text-muted shrink-0'}>{item.icon}</span>
                  <span className="truncate">{item.label}</span>
                </span>
                {item.badge !== undefined && (
                  <span
                    className={`text-[10px] font-mono px-1.5 py-0.2 rounded-md shrink-0 ${
                      active ? 'bg-background/80 text-foreground' : 'text-muted'
                    }`}
                  >
                    {item.badge}
                  </span>
                )}
              </button>
            );
          })}
        </div>

        {sidebarGroups.map((group) => {
          const isCollapsed = collapsedGroups[group.id] ?? !(group.defaultOpen ?? true);
          const filteredGroupItems = kw
            ? group.items.filter(
                (x) =>
                  x.title.toLowerCase().includes(kw) ||
                  (x.subtitle || '').toLowerCase().includes(kw) ||
                  (x.meta || '').toLowerCase().includes(kw)
              )
            : group.items;

          return (
            <div key={group.id} className="space-y-1 pt-1 border-t border-separator/70">
              <button
                type="button"
                onClick={() => toggleGroup(group.id, group.defaultOpen ?? true)}
                className="w-full px-2 py-1 flex items-center justify-between text-[11px] font-medium text-muted hover:text-foreground transition cursor-pointer"
              >
                <span className="truncate">{group.label}</span>
                <span className="flex items-center gap-1 font-mono text-[10px]">
                  <span>{group.count ?? group.items.length}</span>
                  {isCollapsed ? <ChevronRight className="w-3 h-3" /> : <ChevronDown className="w-3 h-3" />}
                </span>
              </button>

              {!isCollapsed && (
                <div className="space-y-0.5">
                  {filteredGroupItems.length === 0 ? (
                    <div className="px-2.5 py-2 text-[11px] text-muted/70">
                      {group.emptyText || '暂无活动项'}
                    </div>
                  ) : (
                    filteredGroupItems.map((gi) => (
                      <button
                        key={gi.id}
                        type="button"
                        onClick={() => {
                          gi.onSelect();
                          if (isMobileDrawer) setMobileNavOpen(false);
                        }}
                        className={`w-full text-left px-2.5 py-1.5 rounded-lg transition cursor-pointer border ${
                          gi.active
                            ? 'bg-surface-secondary border-border text-foreground'
                            : 'border-transparent hover:bg-surface/65 text-muted hover:text-foreground'
                        }`}
                      >
                        <div className="flex items-center justify-between gap-1.5">
                          <span className="flex items-center gap-1.5 min-w-0 text-xs font-medium text-foreground truncate">
                            {gi.statusColor && (
                              <span className={`w-1.5 h-1.5 rounded-full shrink-0 ${DOT_COLORS[gi.statusColor]}`} />
                            )}
                            <span className="truncate">{gi.title}</span>
                          </span>
                          {gi.meta && <span className="text-[10px] font-mono text-muted shrink-0">{gi.meta}</span>}
                        </div>
                        {gi.subtitle && (
                          <div className="text-[11px] text-muted truncate mt-0.5 pl-3">{gi.subtitle}</div>
                        )}
                      </button>
                    ))
                  )}
                </div>
              )}
            </div>
          );
        })}
      </div>

      {/* 底部用户状态与工具图标坞 (对齐 T3 Code 左下角图标栏) */}
      <div className="border-t border-border p-2 space-y-1.5 shrink-0 bg-[var(--sidebar-bg)]">
        {sidebarFooterTop && <div className="px-1">{sidebarFooterTop}</div>}
        <div className="h-8 px-1 flex items-center justify-between gap-1">
          <div className="flex items-center gap-1 min-w-0">{sidebarUtilityLeft}</div>
          <div className="flex items-center gap-0.5 shrink-0">
            <Tooltip delay={200}>
              <Button
                isIconOnly
                size="sm"
                variant="ghost"
                aria-label={colorMode === 'dark' ? '切换浅色主题' : '切换深色碳黑主题'}
                className="h-7 w-7 min-w-0 text-muted data-[hovered=true]:text-foreground"
                onPress={toggleColorMode}
              >
                {colorMode === 'dark' ? <Sun className="w-3.5 h-3.5" /> : <Moon className="w-3.5 h-3.5" />}
              </Button>
              <Tooltip.Content>
                <p className="text-xs">{colorMode === 'dark' ? '切换至浅色模式' : '切换至碳黑深色模式'}</p>
              </Tooltip.Content>
            </Tooltip>
            {sidebarUtilityRight}
          </div>
        </div>
      </div>
    </div>
  );

  return (
    <div
      className={`${themeClass} ${ambientClass} h-dvh w-full overflow-hidden flex bg-background text-foreground`}
    >
      {/* 1. 桌面端左侧可折叠侧边栏 */}
      {desktopSidebarOpen && (
        <aside className="hidden lg:flex w-[248px] xl:w-[260px] shrink-0 border-r border-border flex-col h-full">
          {renderSidebarContent(false)}
        </aside>
      )}

      {/* 移动端左侧导航抽屉 (HeroUI v3 Drawer) */}
      <Drawer.Backdrop
        isOpen={mobileNavOpen}
        onOpenChange={setMobileNavOpen}
        className={`lg:hidden ${themeClass}`}
      >
        <Drawer.Content placement="left" className="lg:hidden w-[276px] max-w-[84vw]">
          <Drawer.Dialog className="h-full p-0 bg-[var(--sidebar-bg)] border-r border-border rounded-none">
            {renderSidebarContent(true)}
          </Drawer.Dialog>
        </Drawer.Content>
      </Drawer.Backdrop>

      {/* 2. 中央主工作区 (顶栏面包屑 + 主画布 + 底部可折叠实时终端) */}
      <div className="flex-1 min-w-0 flex flex-col h-full overflow-hidden">
        {/* 纤薄面包屑顶栏 (h-11) */}
        <header className="h-11 shrink-0 border-b border-border bg-background/90 backdrop-blur-md px-2.5 sm:px-3.5 flex items-center justify-between gap-2 z-20">
          <div className="flex items-center gap-2 min-w-0">
            {/* 移动端打开左抽屉 */}
            <Button
              isIconOnly
              size="sm"
              variant="ghost"
              aria-label="打开导航菜单"
              className="lg:hidden h-7 w-7 min-w-0 text-muted data-[hovered=true]:text-foreground"
              onPress={() => setMobileNavOpen(true)}
            >
              <PanelLeft className="w-4 h-4" />
            </Button>

            {/* 桌面端当侧栏收起时显示展开按钮 */}
            {!desktopSidebarOpen && (
              <Tooltip delay={200}>
                <Button
                  isIconOnly
                  size="sm"
                  variant="ghost"
                  aria-label="展开左侧边栏"
                  className="hidden lg:inline-flex h-7 w-7 min-w-0 text-muted data-[hovered=true]:text-foreground"
                  onPress={() => setDesktopSidebarOpen(true)}
                >
                  <PanelLeft className="w-4 h-4" />
                </Button>
                <Tooltip.Content>
                  <p className="text-xs">展开侧边栏</p>
                </Tooltip.Content>
              </Tooltip>
            )}

            {/* 面包屑路径: [YP] YGOProSharp / 当前模块 */}
            <div className="flex items-center gap-1.5 min-w-0 text-xs sm:text-[13px]">
              <span
                className={`hidden sm:inline-flex w-4 h-4 rounded text-[9px] font-bold items-center justify-center border shrink-0 ${monogramClass}`}
              >
                {monogram}
              </span>
              <span className="hidden sm:inline text-muted truncate">{workspaceName}</span>
              <span className="hidden sm:inline text-muted/50">/</span>
              <span className="font-semibold text-foreground truncate">{breadcrumbCurrent}</span>
              {statusBadge && <span className="shrink-0 ml-1">{statusBadge}</span>}
            </div>
          </div>

          {/* 顶栏右侧操作区 + 右侧检查器分栏开关 */}
          <div className="flex items-center gap-1.5 shrink-0">
            {headerRightActions}

            {hasInspector && (
              <>
                {/* 桌面端切换右侧分栏 */}
                <Tooltip delay={200}>
                  <Button
                    isIconOnly
                    size="sm"
                    variant={inspectorOpen ? 'secondary' : 'ghost'}
                    aria-label="切换右侧详情分栏"
                    className="hidden lg:inline-flex h-7 w-7 min-w-0"
                    onPress={() => onInspectorOpenChange?.(!inspectorOpen)}
                  >
                    <PanelRight className="w-3.5 h-3.5" />
                  </Button>
                  <Tooltip.Content>
                    <p className="text-xs">{inspectorOpen ? '收起右侧详情栏' : '展开右侧详情栏'}</p>
                  </Tooltip.Content>
                </Tooltip>

                {/* 移动端打开右侧/底部检查器抽屉 */}
                <Button
                  isIconOnly
                  size="sm"
                  variant="secondary"
                  aria-label="打开侧边面板"
                  className="lg:hidden h-7 w-7 min-w-0"
                  onPress={() => onMobileInspectorOpenChange?.(true)}
                >
                  <PanelRight className="w-3.5 h-3.5" />
                </Button>
              </>
            )}
          </div>
        </header>

        {/* 主内容滚动区 */}
        <main className="flex-1 overflow-y-auto px-3 sm:px-5 py-4 pb-mobile-nav">
          <div className="max-w-[1400px] mx-auto space-y-4">{children}</div>
        </main>

        {/* 底部可折叠实时服务事件终端栏 (对齐 T3 Code 底部运行日志窗格) */}
        {(consoleStatusText || consoleLogs.length > 0) && (
          <div className="hidden md:flex flex-col border-t border-border bg-[var(--console-bg)] shrink-0">
            <button
              type="button"
              onClick={() => setConsoleExpanded((v) => !v)}
              className="h-7 px-3 flex items-center justify-between gap-3 text-[11px] font-mono text-muted hover:text-foreground transition cursor-pointer"
            >
              <div className="flex items-center gap-2 min-w-0 truncate">
                <Terminal className="w-3.5 h-3.5 text-emerald-400 shrink-0" />
                <span className="text-foreground/90 font-semibold shrink-0">实时事件流</span>
                <span className="text-muted/60">·</span>
                <span className="truncate">
                  {consoleLogs[0]
                    ? `[${consoleLogs[0].time}] ${consoleLogs[0].source}: ${consoleLogs[0].message}`
                    : consoleStatusText || '就绪'}
                </span>
              </div>
              <div className="flex items-center gap-2 shrink-0">
                {consoleStatusText && <span className="text-muted hidden xl:inline">{consoleStatusText}</span>}
                {consoleExpanded ? <ChevronDown className="w-3.5 h-3.5" /> : <ChevronUp className="w-3.5 h-3.5" />}
              </div>
            </button>

            {consoleExpanded && (
              <div className="h-44 border-t border-separator px-3 py-2 overflow-y-auto font-mono text-[11px] leading-relaxed space-y-1">
                {consoleLogs.length === 0 ? (
                  <div className="text-muted py-6 text-center">暂无实时事件记录，当有房间动态或系统广播时将在此实时滚动输出...</div>
                ) : (
                  consoleLogs.map((log) => (
                    <div key={log.id} className="flex items-baseline gap-2 break-all">
                      <span className="text-muted shrink-0">{log.time}</span>
                      <span
                        className={`shrink-0 ${
                          log.level === 'warn'
                            ? 'text-amber-400'
                            : log.level === 'chat'
                              ? 'text-sky-400'
                              : 'text-emerald-400'
                        }`}
                      >
                        {log.level || 'info'}:
                      </span>
                      <span className="text-muted shrink-0">[{log.source}]</span>
                      <span className="text-foreground/90">{log.message}</span>
                    </div>
                  ))
                )}
              </div>
            )}
          </div>
        )}
      </div>

      {/* 3. 桌面端右侧上下文多标签分栏 (对齐 T3 Code 右侧 Diff / Inspector 面板) */}
      {hasInspector && inspectorOpen && (
        <aside
          className={`hidden lg:flex shrink-0 border-l border-border bg-[var(--sidebar-bg)] flex-col h-full overflow-hidden transition-[width] duration-150 ${
            inspectorWide ? 'w-[520px] xl:w-[580px]' : 'w-[370px] xl:w-[410px]'
          }`}
        >
          <div className="h-11 px-2 border-b border-border flex items-center justify-between gap-1 shrink-0">
            <div className="flex items-center gap-1 overflow-x-auto no-scrollbar min-w-0">
              {inspectorTabs.map((t) => {
                const active = t.key === currentInspectorTab?.key;
                return (
                  <button
                    key={t.key}
                    type="button"
                    onClick={() => onChangeInspectorKey?.(t.key)}
                    className={`h-7 px-2.5 rounded-md text-xs flex items-center gap-1.5 shrink-0 transition cursor-pointer border ${
                      active
                        ? 'bg-surface-secondary border-border text-foreground font-semibold'
                        : 'border-transparent text-muted hover:text-foreground hover:bg-surface/60'
                    }`}
                  >
                    {t.icon}
                    <span>{t.label}</span>
                    {t.badge !== undefined && (
                      <span className="text-[10px] font-mono px-1 rounded bg-background/80 text-muted">
                        {t.badge}
                      </span>
                    )}
                  </button>
                );
              })}
            </div>

            <div className="flex items-center gap-0.5 shrink-0">
              <Tooltip delay={200}>
                <Button
                  isIconOnly
                  size="sm"
                  variant="ghost"
                  aria-label={inspectorWide ? '恢复标准宽度' : '加宽侧栏'}
                  className="h-7 w-7 min-w-0 text-muted data-[hovered=true]:text-foreground"
                  onPress={() => setInspectorWide((v) => !v)}
                >
                  {inspectorWide ? <Minimize2 className="w-3.5 h-3.5" /> : <Maximize2 className="w-3.5 h-3.5" />}
                </Button>
                <Tooltip.Content>
                  <p className="text-xs">{inspectorWide ? '标准分栏宽度' : '加宽分栏'}</p>
                </Tooltip.Content>
              </Tooltip>

              <Tooltip delay={200}>
                <Button
                  isIconOnly
                  size="sm"
                  variant="ghost"
                  aria-label="关闭右侧分栏"
                  className="h-7 w-7 min-w-0 text-muted data-[hovered=true]:text-foreground"
                  onPress={() => onInspectorOpenChange?.(false)}
                >
                  <X className="w-3.5 h-3.5" />
                </Button>
                <Tooltip.Content>
                  <p className="text-xs">关闭分栏</p>
                </Tooltip.Content>
              </Tooltip>
            </div>
          </div>

          <div className="flex-1 overflow-y-auto p-3.5">{currentInspectorTab?.content}</div>
        </aside>
      )}

      {/* 移动端右侧检查器底部抽屉 (HeroUI v3 Drawer) */}
      {hasInspector && (
        <Drawer.Backdrop
          isOpen={mobileInspectorOpen}
          onOpenChange={(open) => onMobileInspectorOpenChange?.(open)}
          className={`lg:hidden ${themeClass}`}
        >
          <Drawer.Content placement="bottom" className="lg:hidden">
            <Drawer.Dialog className="max-h-[88dvh] rounded-t-2xl bg-[var(--sidebar-bg)] text-foreground border-t border-border pb-safe flex flex-col">
              <Drawer.Handle />
              <div className="px-3 py-2 border-b border-border flex items-center justify-between gap-2">
                <div className="flex items-center gap-1 overflow-x-auto no-scrollbar">
                  {inspectorTabs.map((t) => {
                    const active = t.key === currentInspectorTab?.key;
                    return (
                      <button
                        key={t.key}
                        type="button"
                        onClick={() => onChangeInspectorKey?.(t.key)}
                        className={`h-7 px-2.5 rounded-md text-xs flex items-center gap-1.5 shrink-0 transition cursor-pointer border ${
                          active
                            ? 'bg-surface-secondary border-border text-foreground font-semibold'
                            : 'border-transparent text-muted'
                        }`}
                      >
                        {t.icon}
                        <span>{t.label}</span>
                      </button>
                    );
                  })}
                </div>
                <Button
                  isIconOnly
                  size="sm"
                  variant="ghost"
                  aria-label="关闭面板"
                  className="h-7 w-7 min-w-0"
                  onPress={() => onMobileInspectorOpenChange?.(false)}
                >
                  <X className="w-4 h-4" />
                </Button>
              </div>
              <Drawer.Body className="p-3.5 overflow-y-auto flex-1">{currentInspectorTab?.content}</Drawer.Body>
            </Drawer.Dialog>
          </Drawer.Content>
        </Drawer.Backdrop>
      )}

      {/* 移动端底部单手拇指导航栏 (md:hidden) */}
      <nav className="md:hidden fixed bottom-0 inset-x-0 z-30 bg-[var(--sidebar-bg)]/95 backdrop-blur-xl border-t border-border pb-safe">
        <div
          className="grid h-13"
          style={{ gridTemplateColumns: `repeat(${Math.max(1, navItems.length)}, minmax(0, 1fr))` }}
        >
          {navItems.map((item) => {
            const active = item.key === activeNavKey;
            return (
              <button
                key={item.key}
                type="button"
                onClick={() => onSelectNav(item.key)}
                className={`relative flex flex-col items-center justify-center gap-0.5 text-[10px] font-medium transition cursor-pointer ${
                  active ? 'text-foreground font-semibold' : 'text-muted active:text-foreground'
                }`}
              >
                {active && <span className="absolute top-0 w-6 h-0.5 rounded-full bg-accent" />}
                <span className={active ? 'text-accent' : ''}>{item.icon}</span>
                <span className="truncate max-w-full px-1">{item.shortLabel}</span>
              </button>
            );
          })}
        </div>
      </nav>

      {modals}
      {toast}
    </div>
  );
}
