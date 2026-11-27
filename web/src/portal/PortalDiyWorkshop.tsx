import React, { useMemo, useState } from 'react';
import {
  Button,
  Card,
  Chip,
  Input,
  Label,
  Spinner,
  Surface,
  Tabs,
  TextField
} from '@heroui/react';
import { Download, Eye, FolderUp, Palette, Send, Sparkles, Wand2 } from 'lucide-react';
import { apiFetch, apiUploadWithProgress, UploadProgressInfo } from '../api';
import { CodeMirrorEditor } from '../components/CodeMirrorEditor';
import {
  CARD_BITS,
  EditableDiyCard,
  generateSmartLuaTemplate
} from '../diy/ygoCardTaxonomy';
import { YgoCardVisualPreview } from '../diy/YgoCardVisualPreview';
import { CardBaseStatsInspector, CardTextAndStringsInspector } from '../diy/YgoCardEditorInspector';
import {
  createInstalledPackageResolver,
  createPreviewTokenResolver,
  formatBytes,
  YpkExplorerModal,
  YpkInspectionReport,
  YpkPackageExplorer
} from '../diy/YpkPackageExplorer';
import { PortalUser } from './portalTypes';

type MakerTab = 'stats' | 'text' | 'script';

export function PortalDiyWorkshop({
  diyPackages,
  portalUser,
  notify
}: {
  diyPackages: any[];
  portalUser: PortalUser | null;
  notify: (t: string, k?: 'ok' | 'err') => void;
}) {
  const [mode, setMode] = useState<'visual' | 'ypk'>('visual');
  const [makerTab, setMakerTab] = useState<MakerTab>('stats');
  const [authorName, setAuthorName] = useState('');
  const [targetPkg, setTargetPkg] = useState('community-diy');
  const [submitting, setSubmitting] = useState(false);

  const [uploadProgress, setUploadProgress] = useState<UploadProgressInfo | null>(null);
  const [previewToken, setPreviewToken] = useState<string | null>(null);
  const [previewReport, setPreviewReport] = useState<YpkInspectionReport | null>(null);
  const [confirmingYpk, setConfirmingYpk] = useState(false);

  const [installedModalPkgId, setInstalledModalPkgId] = useState<string | null>(null);
  const [installedModalReport, setInstalledModalReport] = useState<YpkInspectionReport | null>(null);
  const [loadingInstalledPkg, setLoadingInstalledPkg] = useState<string | null>(null);

  const [draftCard, setDraftCard] = useState<EditableDiyCard>({
    id: 99100001,
    ot: 4,
    alias: 0,
    setcodeHex: '0x0',
    type: CARD_BITS.MONSTER | CARD_BITS.EFFECT,
    level: 4,
    lScale: 0,
    rScale: 0,
    race: 0x2,
    attribute: 0x20,
    attack: 1800,
    defense: 1200,
    linkMarker: 0,
    name: '星尘咏唱师',
    desc: '这个卡名的①的效果1回合只能使用1次。\n①：这张卡召唤·特殊召唤的场合才能发动。自己从卡组抽1张。',
    strings: ['抽1张卡']
  });

  const [luaScript, setLuaScript] = useState<string>(() =>
    generateSmartLuaTemplate({
      id: 99100001,
      ot: 4,
      alias: 0,
      setcodeHex: '0x0',
      type: CARD_BITS.MONSTER | CARD_BITS.EFFECT,
      level: 4,
      lScale: 0,
      rScale: 0,
      race: 0x2,
      attribute: 0x20,
      attack: 1800,
      defense: 1200,
      linkMarker: 0,
      name: '星尘咏唱师',
      desc: ''
    })
  );

  const [localArtworkDataUrl, setLocalArtworkDataUrl] = useState<string | null>(null);

  const previewResolver = useMemo(
    () => (previewToken ? createPreviewTokenResolver('/api/portal', previewToken) : null),
    [previewToken]
  );

  const installedResolver = useMemo(
    () => (installedModalPkgId ? createInstalledPackageResolver('/api/portal', installedModalPkgId) : null),
    [installedModalPkgId]
  );

  const openInstalledPackageExplorer = async (pkgId: string) => {
    setLoadingInstalledPkg(pkgId);
    try {
      const res = await apiFetch<{ report: YpkInspectionReport }>(
        `/api/portal/diy/packages/${encodeURIComponent(pkgId)}/inspect`
      );
      setInstalledModalPkgId(pkgId);
      setInstalledModalReport(res.report);
    } catch (err) {
      notify(err instanceof Error ? err.message : '读取扩展包内部结构失败', 'err');
    } finally {
      setLoadingInstalledPkg(null);
    }
  };

  const handleSelectLocalArtwork = (files: FileList | null) => {
    if (!files || !files.length) return;
    const f = files[0];
    if (f.size > 4 * 1024 * 1024) {
      notify('单张投稿卡图请控制在 4MB 以内', 'err');
      return;
    }
    const reader = new FileReader();
    reader.onload = () => {
      if (typeof reader.result === 'string') {
        setLocalArtworkDataUrl(reader.result);
      }
    };
    reader.readAsDataURL(f);
  };

  const submitVisualCardProposal = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!draftCard.name.trim()) {
      notify('请填写新卡名称', 'err');
      return;
    }
    setSubmitting(true);
    try {
      const res = await apiFetch('/api/portal/diy/proposals', {
        method: 'POST',
        body: JSON.stringify({
          packageId: targetPkg.trim() || 'community-diy',
          authorName: (authorName || portalUser?.username || '').trim() || undefined,
          card: {
            ...draftCard,
            id: Number(draftCard.id)
          },
          luaScript,
          imageBase64: localArtworkDataUrl || undefined
        })
      });
      notify(res.message || 'DIY 卡片已投递至后台审批队列！');
      setDraftCard((prev) => ({
        ...prev,
        id: Number(prev.id) + 1,
        name: `新 DIY 卡 #${Number(prev.id) + 1}`
      }));
      setLocalArtworkDataUrl(null);
    } catch (err) {
      notify(err instanceof Error ? err.message : '投稿失败', 'err');
    } finally {
      setSubmitting(false);
    }
  };

  const handleUploadAndInspectYpk = async (files: FileList | null) => {
    if (!files || files.length === 0) return;
    const file = files[0];
    setUploadProgress({ loaded: 0, total: file.size, percent: 0 });
    try {
      const fd = new FormData();
      fd.append('file', file);
      const res = await apiUploadWithProgress<{
        previewToken: string;
        suggestedPackageId: string;
        report: YpkInspectionReport;
      }>('/api/portal/diy/inspect-ypk', fd, (prog) => setUploadProgress(prog));

      setPreviewToken(res.previewToken);
      setPreviewReport(res.report);
      if (!targetPkg.trim() || targetPkg === 'community-diy') {
        setTargetPkg(res.suggestedPackageId);
      }
      notify(`解析完成：共 ${res.report.totalFiles} 个文件 · ${res.report.cardCount} 张卡片，可在下方直接预览卡面与源码！`);
    } catch (err) {
      notify(err instanceof Error ? err.message : '上传解析 .ypk 失败', 'err');
    } finally {
      setUploadProgress(null);
    }
  };

  const confirmSubmitInspectedYpk = async () => {
    if (!previewToken) return;
    setConfirmingYpk(true);
    try {
      const res = await apiFetch('/api/portal/diy/proposals/import-ypk', {
        method: 'POST',
        body: JSON.stringify({
          previewToken,
          packageId: targetPkg.trim() || undefined,
          authorName: (authorName || portalUser?.username || '').trim() || undefined
        })
      });
      notify(res.message || 'YPK 整包已成功投递至后台审批中心！');
      setPreviewToken(null);
      setPreviewReport(null);
    } catch (err) {
      notify(err instanceof Error ? err.message : '提交投稿失败', 'err');
    } finally {
      setConfirmingYpk(false);
    }
  };

  return (
    <div className="space-y-4 animate-fade-in">
      <YpkExplorerModal
        open={Boolean(installedModalPkgId && installedModalReport)}
        title={`扩展包内部文件与卡片透视 · ${installedModalPkgId}`}
        report={installedModalReport}
        resolver={installedResolver}
        accent="emerald"
        onClose={() => {
          setInstalledModalPkgId(null);
          setInstalledModalReport(null);
        }}
      />

      {/* 1. 本服 DIY 扩展包展厅 (.ypk 透视与导出) */}
      <Card className="rounded-xl p-4 sm:p-5 space-y-4">
        <Card.Header className="p-0 flex-col sm:flex-row sm:items-center justify-between gap-3">
          <div>
            <Card.Title className="text-sm sm:text-base font-bold flex items-center gap-2 text-emerald-400">
              <Download className="w-4 h-4" /> 本服 DIY 扩展卡包展厅 (.ypk 透视与导出)
            </Card.Title>
            <Card.Description className="text-[11px] mt-0.5">
              在线透视每个 <code className="text-emerald-400 font-mono">.ypk</code> 包内的文件树、卡片画廊与脚本，或一键下载放入客户端{' '}
              <code className="text-foreground font-mono">expansions/</code>
            </Card.Description>
          </div>

          {diyPackages.length > 0 && (
            <a
              href="/api/portal/diy/export-all.ypk"
              download
              className="button button--primary h-8 inline-flex items-center justify-center gap-1.5 px-3.5 rounded-lg text-xs shrink-0"
            >
              <Download className="w-3.5 h-3.5" /> 导出全服整合包 (.ypk)
            </a>
          )}
        </Card.Header>

        <Card.Content className="p-0">
          {diyPackages.length > 0 ? (
            <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-3">
              {diyPackages.map((pkg) => (
                <Surface
                  key={pkg.id}
                  variant="secondary"
                  className="rounded-xl p-4 space-y-3 text-xs border border-border flex flex-col justify-between"
                >
                  <div className="space-y-2">
                    <div className="flex items-center justify-between gap-2">
                      <span className="font-bold text-sm text-foreground truncate">{pkg.name}</span>
                      <Chip size="sm" variant="soft" color="accent" className="h-5 px-2 font-mono text-[10px] shrink-0">
                        {pkg.cardCount} 卡 · v{pkg.version}
                      </Chip>
                    </div>
                    <div className="text-[11px] text-muted">
                      包标识: <code className="font-mono text-foreground">{pkg.id}</code> · 作者: {pkg.author || '服主'}
                    </div>
                    {(pkg.sampleCards || []).length > 0 && (
                      <div className="flex flex-wrap gap-1 pt-0.5">
                        {(pkg.sampleCards || []).slice(0, 5).map((sc: any) => (
                          <Chip key={sc.id} size="sm" variant="soft" className="h-5 px-1.5 text-[10px]">
                            {sc.name}
                          </Chip>
                        ))}
                      </div>
                    )}
                  </div>

                  <div className="grid grid-cols-2 gap-2 pt-1">
                    <Button
                      size="sm"
                      variant="secondary"
                      isPending={loadingInstalledPkg === pkg.id}
                      onPress={() => openInstalledPackageExplorer(pkg.id)}
                    >
                      <Eye className="w-3.5 h-3.5" />
                      <span>{loadingInstalledPkg === pkg.id ? '加载中...' : '查看包内文件'}</span>
                    </Button>
                    <a
                      href={`/api/portal/diy/packages/${encodeURIComponent(pkg.id)}/export.ypk`}
                      download
                      className="button button--primary h-8 inline-flex items-center justify-center gap-1.5 rounded-lg text-xs"
                    >
                      <Download className="w-3.5 h-3.5" /> 下载 .ypk
                    </a>
                  </div>
                </Surface>
              ))}
            </div>
          ) : (
            <Surface variant="secondary" className="rounded-xl py-8 px-4 text-center text-xs text-muted border border-border">
              本服当前暂未启用自定义 DIY 扩展包，欢迎在下方工作台在线制作新卡或导入本地 .ypk 扩展包！
            </Surface>
          )}
        </Card.Content>
      </Card>

      {/* 2. 可视化在线制卡器 & .ypk 透视导入工作台 */}
      <Card className="rounded-xl p-4 sm:p-5 space-y-4">
        <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3 border-b border-separator pb-3.5">
          <div>
            <h2 className="text-sm sm:text-base font-bold flex items-center gap-2 text-foreground">
              <Palette className="w-4 h-4 text-amber-400" /> 社区 DIY 制卡工作台 · 实时卡面渲染与 .ypk 透视投稿
            </h2>
            <p className="text-[11px] text-muted mt-0.5">
              支持单卡可视化制作，或上传本地 <code className="text-amber-400 font-mono">.ypk</code>（最大 256MB）先浏览包内全部文件树、卡面与 Lua 源码再零重传投递
            </p>
          </div>

          <Tabs
            selectedKey={mode}
            onSelectionChange={(k) => setMode(String(k) as 'visual' | 'ypk')}
            className="self-start sm:self-auto"
          >
            <Tabs.ListContainer>
              <Tabs.List aria-label="制卡或整包导入切换">
                <Tabs.Tab id="visual" className="text-xs px-3 gap-1.5">
                  <Sparkles className="w-3.5 h-3.5" />
                  <span>可视化在线制卡</span>
                  <Tabs.Indicator />
                </Tabs.Tab>
                <Tabs.Tab id="ypk" className="text-xs px-3 gap-1.5">
                  <FolderUp className="w-3.5 h-3.5" />
                  <span>透视 & 导入 .ypk</span>
                  <Tabs.Indicator />
                </Tabs.Tab>
              </Tabs.List>
            </Tabs.ListContainer>
          </Tabs>
        </div>

        <div className="grid grid-cols-1 sm:grid-cols-2 gap-3 text-xs">
          <TextField fullWidth value={authorName} onChange={setAuthorName}>
            <Label className="text-xs">创作者署名</Label>
            <Input
              variant="secondary"
              placeholder={portalUser ? `默认使用登录账号 (${portalUser.username})` : '填写你的创作者昵称'}
            />
          </TextField>

          <TextField fullWidth value={targetPkg} onChange={setTargetPkg}>
            <Label className="text-xs">投稿目标扩展包 ID</Label>
            <Input variant="secondary" placeholder="如 community-diy" className="font-mono" />
          </TextField>
        </div>

        {mode === 'visual' ? (
          <form onSubmit={submitVisualCardProposal} className="grid grid-cols-1 lg:grid-cols-12 gap-4 items-start pt-1">
            <Surface
              variant="secondary"
              className="lg:col-span-5 lg:sticky lg:top-4 rounded-xl p-4 space-y-3.5 border border-border"
            >
              <div className="flex items-center justify-between text-xs">
                <span className="font-semibold text-foreground">实时卡面效果预览</span>
                <span className="text-[11px] text-muted">点击卡图框上传 PNG / JPG</span>
              </div>

              <YgoCardVisualPreview
                card={draftCard}
                imageUrl={localArtworkDataUrl}
                canUploadImage={true}
                onSelectImageFiles={handleSelectLocalArtwork}
              />

              <Button type="submit" variant="primary" fullWidth isPending={submitting}>
                {submitting ? <Spinner color="current" size="sm" /> : <Send className="w-3.5 h-3.5" />}
                <span>{submitting ? '正在执行安全预检并投递...' : '提交单卡（含卡图与 Lua）至审批队列'}</span>
              </Button>
            </Surface>

            <div className="lg:col-span-7 space-y-3.5">
              <Tabs
                selectedKey={makerTab}
                onSelectionChange={(k) => setMakerTab(String(k) as MakerTab)}
                className="w-full"
              >
                <Tabs.ListContainer>
                  <Tabs.List aria-label="卡片属性与脚本分栏" className="w-full">
                    <Tabs.Tab id="stats" className="flex-1 text-xs">
                      1. 卡种 / 属性 / 箭头刻度
                      <Tabs.Indicator />
                    </Tabs.Tab>
                    <Tabs.Tab id="text" className="flex-1 text-xs">
                      2. 效果文本 & 提示串
                      <Tabs.Indicator />
                    </Tabs.Tab>
                    <Tabs.Tab id="script" className="flex-1 text-xs">
                      3. Lua 脚本编写
                      <Tabs.Indicator />
                    </Tabs.Tab>
                  </Tabs.List>
                </Tabs.ListContainer>
              </Tabs>

              {makerTab === 'stats' && (
                <CardBaseStatsInspector card={draftCard} onChange={setDraftCard} accent="emerald" />
              )}

              {makerTab === 'text' && (
                <CardTextAndStringsInspector
                  card={draftCard}
                  onChange={setDraftCard}
                  accent="emerald"
                  showHintStrings={true}
                />
              )}

              {makerTab === 'script' && (
                <div className="space-y-3 text-xs">
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <span className="text-muted">
                      支持完整 <code className="text-emerald-400 font-mono">Duel.* / Effect.*</code> 自动补全与安全检查
                    </span>
                    <Button
                      size="sm"
                      variant="secondary"
                      onPress={() => {
                        setLuaScript(generateSmartLuaTemplate(draftCard));
                        notify('已按当前选定卡种生成标准 Lua 脚本骨架');
                      }}
                    >
                      <Wand2 className="w-3.5 h-3.5 text-amber-400" /> 按当前卡种生成 Lua 骨架
                    </Button>
                  </div>

                  <CodeMirrorEditor
                    value={luaScript}
                    onChange={setLuaScript}
                    language="lua"
                    accent="emerald"
                    title={`script/c${draftCard.id}.lua`}
                    minHeight="320px"
                    maxHeight="480px"
                  />
                </div>
              )}
            </div>
          </form>
        ) : (
          <div className="space-y-4">
            <Surface
              variant="secondary"
              className="rounded-xl p-5 border border-dashed border-border text-center space-y-3"
            >
              <div className="text-sm font-bold text-foreground">
                选择本地 <code className="text-amber-400 font-mono">.ypk / .zip</code> 扩展包 · 先透视预览包内文件与实体卡面，再一键投稿
              </div>
              <p className="text-xs text-muted">
                支持最大 256MB 大容量客户端卡包（含高清卡图、多脚本、主题卡组与音效文档），选择后立即解析包内完整目录树与卡片画廊
              </p>

              <div className="flex flex-wrap items-center justify-center gap-3">
                <label
                  className={`button button--primary h-9 px-5 rounded-lg inline-flex items-center justify-center gap-2 text-xs cursor-pointer ${
                    uploadProgress ? 'opacity-60 pointer-events-none' : ''
                  }`}
                >
                  <FolderUp className="w-4 h-4" />
                  <span>
                    {uploadProgress
                      ? `正在上传并透视解析 (${uploadProgress.percent}%)...`
                      : previewReport
                        ? '重新选择其他 .ypk / .zip 文件'
                        : '选择本地 .ypk / .zip 文件透视预览'}
                  </span>
                  <input
                    type="file"
                    accept=".ypk,.zip"
                    className="hidden"
                    onChange={(e) => {
                      handleUploadAndInspectYpk(e.target.files);
                      e.target.value = '';
                    }}
                  />
                </label>
              </div>

              {uploadProgress && (
                <div className="max-w-md mx-auto space-y-1 pt-1">
                  <div className="h-2 rounded-full bg-background overflow-hidden border border-border">
                    <div
                      className="h-full bg-accent transition-all duration-150"
                      style={{ width: `${uploadProgress.percent}%` }}
                    />
                  </div>
                  <div className="text-[11px] font-mono text-muted">
                    {uploadProgress.percent}% · {formatBytes(uploadProgress.loaded)} / {formatBytes(uploadProgress.total)}
                  </div>
                </div>
              )}
            </Surface>

            {previewReport && previewResolver && (
              <YpkPackageExplorer
                report={previewReport}
                resolver={previewResolver}
                accent="emerald"
                actionFooter={
                  <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3">
                    <span className="text-muted">
                      已缓存就绪：点击右侧按钮即可将整包（含全部 {previewReport.cardCount} 张卡、{previewReport.scriptCount} 个脚本与{' '}
                      {previewReport.imageCount} 张卡图）零重传投递至后台审批队列
                    </span>
                    <Button
                      variant="primary"
                      isDisabled={!previewReport.isSafe}
                      isPending={confirmingYpk}
                      onPress={confirmSubmitInspectedYpk}
                      className="shrink-0"
                    >
                      {confirmingYpk ? <Spinner color="current" size="sm" /> : <Send className="w-3.5 h-3.5" />}
                      <span>{confirmingYpk ? '正在入队...' : '确认投递此 .ypk 整包至审批中心'}</span>
                    </Button>
                  </div>
                }
              />
            )}
          </div>
        )}
      </Card>
    </div>
  );
}
