import React, { useState } from 'react';
import { Lock } from 'lucide-react';
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
    <div className="fixed inset-0 z-50 bg-black/70 backdrop-blur-xs flex items-center justify-center p-4">
      <form onSubmit={submit} className="w-full max-w-md bg-slate-900 border border-slate-700 rounded-2xl p-6 shadow-2xl space-y-4 text-xs">
        <div className="flex items-center justify-between">
          <div className="font-bold text-sm flex items-center gap-2 text-emerald-300">
            <Lock className="w-4 h-4" />
            {mode === 'register' ? '注册新玩家账号' : mode === 'login' ? '玩家账号登录' : `修改账号设置 (${currentUser?.username})`}
          </div>
          <button type="button" onClick={onClose} className="text-slate-400 hover:text-white cursor-pointer">
            关闭
          </button>
        </div>

        {mode !== 'password' && (
          <div className="grid grid-cols-2 gap-1 bg-slate-950 p-1 rounded-lg border border-slate-800">
            <button
              type="button"
              onClick={() => {
                setError('');
                onSwitchMode('login');
              }}
              className={`py-1.5 rounded-md font-semibold cursor-pointer ${
                mode === 'login' ? 'bg-emerald-600 text-white' : 'text-slate-400'
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
              className={`py-1.5 rounded-md font-semibold cursor-pointer ${
                mode === 'register' ? 'bg-emerald-600 text-white' : 'text-slate-400'
              }`}
            >
              免费注册账号
            </button>
          </div>
        )}

        {error && <div className="p-2.5 rounded bg-rose-950/70 border border-rose-500/40 text-rose-200">{error}</div>}

        {mode === 'password' ? (
          <>
            <div>
              <label className="block text-slate-400 mb-1">当前原密码（必填校验）</label>
              <input
                type="password"
                value={currentPassword}
                onChange={(e) => setCurrentPassword(e.target.value)}
                className="w-full px-3 py-2 rounded-lg bg-slate-950 border border-slate-700"
                required
              />
            </div>
            <div>
              <label className="block text-slate-400 mb-1">自定义新用户名（选填，留空保持 {currentUser?.username}）</label>
              <input
                value={newUsername}
                onChange={(e) => setNewUsername(e.target.value)}
                placeholder={`留空保持 ${currentUser?.username}`}
                className="w-full px-3 py-2 rounded-lg bg-slate-950 border border-slate-700"
              />
            </div>
            <div>
              <label className="block text-slate-400 mb-1">自定义新密码（最少 6 位，留空保持原密码）</label>
              <input
                type="password"
                minLength={6}
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                placeholder="至少 6 位字符"
                className="w-full px-3 py-2 rounded-lg bg-slate-950 border border-slate-700"
              />
            </div>
          </>
        ) : (
          <>
            <div>
              <label className="block text-slate-400 mb-1">账号用户名（2~32 位字符）</label>
              <input
                value={username}
                onChange={(e) => setUsername(e.target.value)}
                placeholder="如 Duelist_Yugi"
                minLength={2}
                maxLength={32}
                className="w-full px-3 py-2 rounded-lg bg-slate-950 border border-slate-700"
                required
              />
            </div>
            <div>
              <label className="block text-slate-400 mb-1">登录密码（最少 6 位字符）</label>
              <input
                type="password"
                minLength={6}
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                placeholder="至少 6 位字符"
                className="w-full px-3 py-2 rounded-lg bg-slate-950 border border-slate-700"
                required
              />
            </div>
            {mode === 'register' && (
              <div>
                <label className="block text-slate-400 mb-1">确认密码</label>
                <input
                  type="password"
                  minLength={6}
                  value={confirmPw}
                  onChange={(e) => setConfirmPw(e.target.value)}
                  placeholder="再次输入密码"
                  className="w-full px-3 py-2 rounded-lg bg-slate-950 border border-slate-700"
                  required
                />
              </div>
            )}
          </>
        )}

        <button
          disabled={loading}
          className="w-full py-2.5 rounded-lg bg-emerald-600 hover:bg-emerald-500 disabled:opacity-50 font-semibold text-sm text-white cursor-pointer"
        >
          {loading ? '正在提交...' : mode === 'register' ? '立即注册并登录' : mode === 'login' ? '安全登录' : '保存账号设置'}
        </button>
      </form>
    </div>
  );
}
