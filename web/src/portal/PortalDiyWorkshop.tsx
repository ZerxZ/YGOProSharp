import React, { useMemo, useState } from 'react';
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

  // 本地 .ypk 上传透视状态
  const [uploadProgress, setUploadProgress] = useState<UploadProgressInfo | null>(null);
  const [previewToken, setPreviewToken] = useState<string | null>(null);
  const [previewReport, setPreviewReport] = useState<YpkInspectionReport | null>(null);
  const [confirmingYpk, setConfirmingYpk] = useState(false);

  // 已启用扩展包弹窗查看器状态
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
    <div className="space-y-5 animate-fade-in">
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

      {/* 1. 本服 DIY 扩展包画廊与一键下载 .ypk */}
      <div className="glass-card rounded-2xl p-4 sm:p-5 space-y-4">
        <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3">
          <div>
            <h2 className="text-sm sm:text-base font-bold flex items-center gap-2 text-emerald-300">
              <Download className="w-4 h-4" /> 本服 DIY 扩展卡包展厅 (.ypk 透视与导出)
            </h2>
            <p className="text-[11px] text-slate-400 mt-0.5">
              支持在线查看每个 <code className="text-emerald-300 font-mono">.ypk</code> 包内的文件树、卡片画廊与脚本，或一键下载放入客户端{' '}
              <code className="text-slate-300 font-mono">expansions/</code>
            </p>
          </div>

          {diyPackages.length > 0 && (
            <a
              href="/api/portal/diy/export-all.ypk"
              download
              className="min-h-[38px] inline-flex items-center justify-center gap-1.5 px-4 py-2 rounded-xl bg-gradient-to-r from-emerald-600 to-teal-500 hover:from-emerald-500 hover:to-teal-400 text-white text-xs font-semibold shadow-lg shadow-emerald-950/50 shrink-0"
            >
              <Download className="w-3.5 h-3.5" /> 一键导出全服整合包 (.ypk)
            </a>
          )}
        </div>

        {diyPackages.length > 0 ? (
          <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-3">
            {diyPackages.map((pkg) => (
              <div
                key={pkg.id}
                className="glass-subcard rounded-2xl p-4 space-y-3 text-xs flex flex-col justify-between"
              >
                <div className="space-y-2">
                  <div className="flex items-center justify-between gap-2">
                    <span className="font-bold text-sm text-slate-100 truncate">{pkg.name}</span>
                    <span className="px-2 py-0.5 rounded-full bg-emerald-500/15 border border-emerald-500/30 font-mono text-[10px] text-emerald-300 shrink-0">
                      {pkg.cardCount} 张卡 · v{pkg.version}
                    </span>
                  </div>
                  <div className="text-[11px] text-slate-400">
                    包标识: <code className="font-mono text-slate-300">{pkg.id}</code> · 作者: {pkg.author || '服主'}
                  </div>
                  {(pkg.sampleCards || []).length > 0 && (
                    <div className="flex flex-wrap gap-1.5 pt-0.5">
                      {(pkg.sampleCards || []).slice(0, 5).map((sc: any) => (
                        <span
                          key={sc.id}
                          className="px-2 py-0.5 rounded-md bg-slate-900 border border-slate-800 text-[10px] text-slate-300"
                        >
                          {sc.name}
                        </span>
                      ))}
                    </div>
                  )}
                </div>

                <div className="grid grid-cols-2 gap-2 pt-1">
                  <button
                    type="button"
                    onClick={() => openInstalledPackageExplorer(pkg.id)}
                    className="inline-flex items-center justify-center gap-1.5 py-2 rounded-xl bg-slate-900 hover:bg-slate-800 border border-slate-700 text-emerald-300 font-semibold cursor-pointer"
                  >
                    <Eye className="w-3.5 h-3.5" />
                    <span>{loadingInstalledPkg === pkg.id ? '加载中...' : '查看包内文件'}</span>
                  </button>
                  <a
                    href={`/api/portal/diy/packages/${encodeURIComponent(pkg.id)}/export.ypk`}
                    download
                    className="inline-flex items-center justify-center gap-1.5 py-2 rounded-xl bg-emerald-600 hover:bg-emerald-500 text-white font-semibold transition"
                  >
                    <Download className="w-3.5 h-3.5" /> 下载 .ypk
                  </a>
                </div>
              </div>
            ))}
          </div>
        ) : (
          <div className="glass-subcard rounded-2xl py-8 text-center text-xs text-slate-500">
            本服当前暂未启用自定义 DIY 扩展包，欢迎在下方可视化制卡工坊制作或导入你的第一个 .ypk 扩展包！
          </div>
        )}
      </div>

      {/* 2. 可视化在线制卡器 & .ypk 透视导入工坊 */}
      <div className="glass-card rounded-2xl p-4 sm:p-5 space-y-4">
        <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3 border-b border-slate-800/80 pb-3.5">
          <div>
            <h2 className="text-sm sm:text-base font-bold flex items-center gap-2 text-amber-300">
              <Palette className="w-4 h-4" /> 社区 DIY 制卡工坊 · 实时卡面预览与 .ypk 透视投稿
            </h2>
            <p className="text-[11px] text-slate-400 mt-0.5">
              支持单卡在线制卡，或选择本地 <code className="text-amber-300 font-mono">.ypk</code>（最大 256MB）先在线可视化查看包内文件、卡面与 Lua 脚本再一键投递
            </p>
          </div>

          <div className="inline-flex p-1 rounded-xl bg-slate-950 border border-slate-800 text-xs self-start sm:self-auto">
            <button
              type="button"
              onClick={() => setMode('visual')}
              className={`px-3 py-1.5 rounded-lg font-semibold flex items-center gap-1.5 transition cursor-pointer ${
                mode === 'visual' ? 'bg-emerald-600 text-white shadow' : 'text-slate-400 hover:text-slate-200'
              }`}
            >
              <Sparkles className="w-3.5 h-3.5" /> 可视化在线制卡
            </button>
            <button
              type="button"
              onClick={() => setMode('ypk')}
              className={`px-3 py-1.5 rounded-lg font-semibold flex items-center gap-1.5 transition cursor-pointer ${
                mode === 'ypk' ? 'bg-amber-500 text-slate-950 shadow' : 'text-slate-400 hover:text-slate-200'
              }`}
            >
              <FolderUp className="w-3.5 h-3.5" /> 透视 & 导入 .ypk 整包
            </button>
          </div>
        </div>

        <div className="grid grid-cols-1 sm:grid-cols-2 gap-3 text-xs">
          <div>
            <label className="block text-[11px] text-slate-400 mb-1">创作者署名</label>
            <input
              value={authorName}
              onChange={(e) => setAuthorName(e.target.value)}
              placeholder={portalUser ? `默认使用登录账号 (${portalUser.username})` : '填写你的创作者昵称'}
              className="w-full px-3.5 py-2 input-modern"
            />
          </div>
          <div>
            <label className="block text-[11px] text-slate-400 mb-1">投稿目标扩展包 ID</label>
            <input
              value={targetPkg}
              onChange={(e) => setTargetPkg(e.target.value)}
              placeholder="如 community-diy"
              className="w-full px-3.5 py-2 input-modern font-mono"
            />
          </div>
        </div>

        {mode === 'visual' ? (
          <form onSubmit={submitVisualCardProposal} className="grid grid-cols-1 lg:grid-cols-12 gap-5 items-start pt-1">
            <div className="lg:col-span-5 lg:sticky lg:top-20 glass-subcard rounded-2xl p-4 space-y-3.5 bg-[radial-gradient(circle_at_50%_20%,rgba(16,185,129,0.14),transparent_72%)]">
              <div className="flex items-center justify-between text-xs">
                <span className="font-bold text-emerald-300">实时卡面效果预览</span>
                <span className="text-[10px] text-slate-400">点击卡图框上传 PNG / JPG</span>
              </div>

              <YgoCardVisualPreview
                card={draftCard}
                imageUrl={localArtworkDataUrl}
                canUploadImage={true}
                onSelectImageFiles={handleSelectLocalArtwork}
              />

              <button
                disabled={submitting}
                className="w-full min-h-[44px] py-2.5 rounded-xl bg-gradient-to-r from-amber-500 to-orange-500 hover:from-amber-400 hover:to-orange-400 disabled:opacity-50 text-slate-950 font-black text-xs sm:text-sm flex items-center justify-center gap-1.5 shadow-lg shadow-amber-950/50 cursor-pointer"
              >
                <Send className="w-4 h-4" />
                <span>{submitting ? '正在执行安全预检并投递...' : '提交单卡（含卡图与 Lua）至审批队列'}</span>
              </button>
            </div>

            <div className="lg:col-span-7 space-y-4">
              <div className="flex items-center gap-1.5 p-1 rounded-xl bg-slate-950 border border-slate-800">
                {(
                  [
                    ['stats', '1. 卡种 / 属性 / 箭头刻度'],
                    ['text', '2. 效果文本 & 提示串'],
                    ['script', '3. Lua 脚本编写']
                  ] as [MakerTab, string][]
                ).map(([k, label]) => (
                  <button
                    key={k}
                    type="button"
                    onClick={() => setMakerTab(k)}
                    className={`flex-1 py-2 px-2.5 rounded-lg text-xs font-semibold transition cursor-pointer ${
                      makerTab === k ? 'bg-emerald-600 text-white shadow' : 'text-slate-400 hover:text-slate-200'
                    }`}
                  >
                    {label}
                  </button>
                ))}
              </div>

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
                    <span className="text-slate-400">
                      支持完整 <code className="text-emerald-300 font-mono">Duel.* / Effect.*</code> 自动补全与安全检查
                    </span>
                    <button
                      type="button"
                      onClick={() => {
                        setLuaScript(generateSmartLuaTemplate(draftCard));
                        notify('已按当前选定卡种生成标准 Lua 脚本骨架');
                      }}
                      className="px-3 py-1.5 rounded-xl glass-subcard hover:border-emerald-400/50 text-emerald-200 font-medium flex items-center gap-1.5 cursor-pointer"
                    >
                      <Wand2 className="w-3.5 h-3.5 text-amber-400" /> 按当前卡种生成 Lua 骨架
                    </button>
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
            <div className="glass-subcard rounded-2xl p-5 border border-dashed border-amber-500/40 text-center space-y-3">
              <div className="text-sm font-bold text-slate-100">
                选择本地 <code className="text-amber-300 font-mono">.ypk / .zip</code> 扩展包 · 先透视预览包内文件与实体卡面，再一键投稿
              </div>
              <p className="text-xs text-slate-400">
                支持最大 256MB 大容量客户端卡包（含高清卡图、多脚本、主题卡组与音效文档），选择后将展示包内完整目录树与卡片画廊
              </p>

              <div className="flex flex-wrap items-center justify-center gap-3">
                <label
                  className={`inline-flex min-h-[44px] items-center justify-center gap-2 px-6 py-2.5 rounded-xl bg-amber-500 hover:bg-amber-400 text-slate-950 text-xs sm:text-sm font-black shadow-lg cursor-pointer ${
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
                  <div className="h-2 rounded-full bg-slate-900 overflow-hidden border border-slate-800">
                    <div
                      className="h-full bg-gradient-to-r from-amber-500 to-emerald-400 transition-all duration-150"
                      style={{ width: `${uploadProgress.percent}%` }}
                    />
                  </div>
                  <div className="text-[11px] font-mono text-amber-200">
                    {uploadProgress.percent}% · {formatBytes(uploadProgress.loaded)} / {formatBytes(uploadProgress.total)}
                  </div>
                </div>
              )}
            </div>

            {previewReport && previewResolver && (
              <YpkPackageExplorer
                report={previewReport}
                resolver={previewResolver}
                accent="emerald"
                actionFooter={
                  <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3">
                    <span className="text-slate-300">
                      已缓存就绪：点击右侧按钮即可将整包（含全部 {previewReport.cardCount} 张卡、{previewReport.scriptCount} 个脚本与{' '}
                      {previewReport.imageCount} 张卡图）零重传投递至后台审批队列
                    </span>
                    <button
                      type="button"
                      disabled={!previewReport.isSafe || confirmingYpk}
                      onClick={confirmSubmitInspectedYpk}
                      className="min-h-[40px] px-5 py-2 rounded-xl bg-gradient-to-r from-emerald-600 to-teal-500 hover:from-emerald-500 hover:to-teal-400 disabled:opacity-45 text-white font-bold flex items-center justify-center gap-1.5 shadow-lg shrink-0 cursor-pointer"
                    >
                      <Send className="w-4 h-4" />
                      <span>{confirmingYpk ? '正在入队...' : '确认投递此 .ypk 整包至审批中心'}</span>
                    </button>
                  </div>
                }
              />
            )}
          </div>
        )}
      </div>
    </div>
  );
}
