import React, { useState } from 'react';
import { Eye, EyeOff, Lock, X } from 'lucide-react';
import { apiFetch } from '../api';
import type { PortalUser } from './portalTypes';

export function PortalAuthModal({
  mode,
  currentUser,
  onSwitchMode,
  onClose,
  onAuthenticated
}: {
  mode: 'login' | 'register' | 'password';
  currentUser: PortalUser | null;
  onSwitchMode: (m: 'login' | 'register' | 'password') => void;
  onClose: () => void;
  onAuthenticated: (u: PortalUser, msg: string) => void;
}) {
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [confirmPw, setConfirmPw] = useState('');
  const [currentPassword, setCurrentPassword] = useState('');
  const [newUsername, setNewUsername] = useState('');
  const [showPw, setShowPw] = useState(false);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError('');
    setLoading(true);
    try {
      if (mode === 'register') {
        if (password.length < 6) {
          setError('密码长度至少需要 6 位字符。');
          setLoading(false);
          return;
        }
        if (password !== confirmPw) {
          setError('两次输入的密码不一致。');
          setLoading(false);
          return;
        }
        const res = await apiFetch<{ user: PortalUser; message: string }>('/api/portal/auth/register', {
          method: 'POST',
          body: JSON.stringify({ username: username.trim(), password })
        });
        onAuthenticated(res.user, res.message || '注册并登录成功！');
      } else if (mode === 'login') {
        const res = await apiFetch<{ user: PortalUser; message: string }>('/api/portal/auth/login', {
          method: 'POST',
          body: JSON.stringify({ username: username.trim(), password })
        });
        onAuthenticated(res.user, res.message || '登录成功！');
      } else {
        if (password && password.length < 6) {
          setError('新密码长度至少需要 6 位字符。');
          setLoading(false);
          return;
        }
        const res = await apiFetch<{ user: PortalUser; message: string }>('/api/portal/auth/change-password', {
          method: 'POST',
          body: JSON.stringify({
            currentPassword,
            newUsername: newUsername.trim() || undefined,
            newPassword: password || undefined
          })
        });
        if (res.user) {
          onAuthenticated(res.user, res.message || '账号设置已更新');
        } else {
          onClose();
        }
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : '操作失败');
    } finally {
      setLoading(false);
    }
  };

  return (
    <div
      className="fixed inset-0 z-50 bg-black/75 backdrop-blur-sm flex items-end sm:items-center justify-center sm:p-4"
      onClick={onClose}
    >
      <form
        onClick={(e) => e.stopPropagation()}
        onSubmit={submit}
        className="w-full sm:max-w-md bg-slate-900 border-t sm:border border-slate-700/80 rounded-t-3xl sm:rounded-2xl p-6 pb-safe shadow-2xl space-y-4 text-xs animate-slide-up"
      >
        <div className="sm:hidden flex justify-center -mt-2 mb-1">
          <div className="w-10 h-1 rounded-full bg-slate-700" />
        </div>

        <div className="flex items-center justify-between">
          <div className="font-bold text-base flex items-center gap-2 text-emerald-300">
            <div className="w-8 h-8 rounded-xl bg-emerald-500/15 border border-emerald-500/30 flex items-center justify-center">
              <Lock className="w-4 h-4" />
            </div>
            <span>
              {mode === 'register' ? '注册玩家通行证' : mode === 'login' ? '玩家账号登录' : `账号设置 (${currentUser?.username})`}
            </span>
          </div>
          <button
            type="button"
            onClick={onClose}
            className="p-2 rounded-xl bg-slate-800/80 hover:bg-slate-700 text-slate-400 hover:text-white cursor-pointer"
          >
            <X className="w-4 h-4" />
          </button>
        </div>

        {mode !== 'password' && (
          <div className="grid grid-cols-2 gap-1 bg-slate-950 p-1 rounded-xl border border-slate-800">
            <button
              type="button"
              onClick={() => {
                setError('');
                onSwitchMode('login');
              }}
              className={`py-2 rounded-lg font-semibold transition cursor-pointer ${
                mode === 'login' ? 'bg-emerald-600 text-white shadow' : 'text-slate-400 hover:text-slate-200'
              }`}
            >
              已有账号登录
            </button>
            <button
              type="button"
              onClick={() => {
                setError('');
                onSwitchMode('register');
              }}
              className={`py-2 rounded-lg font-semibold transition cursor-pointer ${
                mode === 'register' ? 'bg-emerald-600 text-white shadow' : 'text-slate-400 hover:text-slate-200'
              }`}
            >
              10 秒免费注册
            </button>
          </div>
        )}

        {error && (
          <div className="p-3 rounded-xl bg-rose-950/70 border border-rose-500/40 text-rose-200 leading-relaxed">{error}</div>
        )}

        {mode === 'password' ? (
          <div className="space-y-3">
            <div>
              <label className="block text-slate-400 mb-1">当前原密码（必填校验）</label>
              <input
                type={showPw ? 'text' : 'password'}
                value={currentPassword}
                onChange={(e) => setCurrentPassword(e.target.value)}
                className="w-full px-3.5 py-2.5 input-modern"
                required
              />
            </div>
            <div>
              <label className="block text-slate-400 mb-1">自定义新用户名（选填，留空保持 {currentUser?.username}）</label>
              <input
                value={newUsername}
                onChange={(e) => setNewUsername(e.target.value)}
                placeholder={`留空保持 ${currentUser?.username}`}
                className="w-full px-3.5 py-2.5 input-modern"
              />
            </div>
            <div>
              <label className="block text-slate-400 mb-1">自定义新密码（最少 6 位，留空保持原密码）</label>
              <div className="relative">
                <input
                  type={showPw ? 'text' : 'password'}
                  minLength={6}
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  placeholder="至少 6 位字符"
                  className="w-full px-3.5 py-2.5 pr-10 input-modern"
                />
                <button
                  type="button"
                  onClick={() => setShowPw((v) => !v)}
                  className="absolute right-2.5 top-1/2 -translate-y-1/2 p-1 text-slate-400 hover:text-slate-200 cursor-pointer"
                >
                  {showPw ? <EyeOff className="w-4 h-4" /> : <Eye className="w-4 h-4" />}
                </button>
              </div>
            </div>
          </div>
        ) : (
          <div className="space-y-3">
            <div>
              <label className="block text-slate-400 mb-1">玩家用户名（2~32 位字符）</label>
              <input
                value={username}
                onChange={(e) => setUsername(e.target.value)}
                placeholder="如 Duelist_Yugi"
                minLength={2}
                maxLength={32}
                autoFocus
                className="w-full px-3.5 py-2.5 input-modern"
                required
              />
            </div>
            <div>
              <label className="block text-slate-400 mb-1">登录密码（最少 6 位字符，Argon2id 加密存储）</label>
              <div className="relative">
                <input
                  type={showPw ? 'text' : 'password'}
                  minLength={6}
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  placeholder="至少 6 位字符"
                  className="w-full px-3.5 py-2.5 pr-10 input-modern"
                  required
                />
                <button
                  type="button"
                  onClick={() => setShowPw((v) => !v)}
                  className="absolute right-2.5 top-1/2 -translate-y-1/2 p-1 text-slate-400 hover:text-slate-200 cursor-pointer"
                >
                  {showPw ? <EyeOff className="w-4 h-4" /> : <Eye className="w-4 h-4" />}
                </button>
              </div>
            </div>
            {mode === 'register' && (
              <div>
                <label className="block text-slate-400 mb-1">确认密码</label>
                <input
                  type={showPw ? 'text' : 'password'}
                  minLength={6}
                  value={confirmPw}
                  onChange={(e) => setConfirmPw(e.target.value)}
                  placeholder="再次输入密码"
                  className="w-full px-3.5 py-2.5 input-modern"
                  required
                />
              </div>
            )}
          </div>
        )}

        <button
          disabled={loading}
          className="w-full min-h-[44px] py-2.5 rounded-xl bg-gradient-to-r from-emerald-600 to-teal-500 hover:from-emerald-500 hover:to-teal-400 disabled:opacity-50 font-semibold text-sm text-white shadow-lg shadow-emerald-950/50 cursor-pointer"
        >
          {loading ? '正在提交...' : mode === 'register' ? '立即注册并登录' : mode === 'login' ? '安全登录' : '保存账号设置'}
        </button>
      </form>
    </div>
  );
}
