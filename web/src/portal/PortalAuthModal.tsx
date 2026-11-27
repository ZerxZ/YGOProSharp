import React, { useState } from 'react';
import {
  Alert,
  Button,
  Description,
  Input,
  Label,
  Modal,
  Spinner,
  Tabs,
  TextField
} from '@heroui/react';
import { Eye, EyeOff, Lock } from 'lucide-react';
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
    <Modal.Backdrop
      isOpen
      onOpenChange={(open) => {
        if (!open) onClose();
      }}
      className="portal-theme"
    >
      <Modal.Container placement="auto" size="sm">
        <Modal.Dialog className="sm:max-w-md border border-border">
          <Modal.CloseTrigger aria-label="关闭认证弹窗" />
          <Modal.Header>
            <Modal.Icon className="bg-accent-soft text-accent-soft-foreground">
              <Lock className="size-4" />
            </Modal.Icon>
            <Modal.Heading className="text-base font-bold">
              {mode === 'register'
                ? '注册玩家通行证'
                : mode === 'login'
                  ? '玩家账号登录'
                  : `账号与密码设置 (${currentUser?.username})`}
            </Modal.Heading>
          </Modal.Header>

          <Modal.Body className="space-y-4 pt-1">
            {mode !== 'password' && (
              <Tabs
                selectedKey={mode}
                onSelectionChange={(k) => {
                  setError('');
                  onSwitchMode(String(k) as 'login' | 'register');
                }}
                className="w-full"
              >
                <Tabs.ListContainer>
                  <Tabs.List aria-label="选择登录或注册" className="w-full">
                    <Tabs.Tab id="login" className="flex-1 text-xs font-semibold">
                      已有账号登录
                      <Tabs.Indicator />
                    </Tabs.Tab>
                    <Tabs.Tab id="register" className="flex-1 text-xs font-semibold">
                      10 秒免费注册
                      <Tabs.Indicator />
                    </Tabs.Tab>
                  </Tabs.List>
                </Tabs.ListContainer>
              </Tabs>
            )}

            {error && (
              <Alert status="danger">
                <Alert.Indicator />
                <Alert.Content>
                  <Alert.Description className="text-xs">{error}</Alert.Description>
                </Alert.Content>
              </Alert>
            )}

            <form id="portal-auth-form" onSubmit={submit} className="space-y-3.5 text-xs">
              {mode === 'password' ? (
                <>
                  <TextField
                    fullWidth
                    isRequired
                    type={showPw ? 'text' : 'password'}
                    value={currentPassword}
                    onChange={setCurrentPassword}
                  >
                    <Label className="text-xs">当前原密码（必填校验）</Label>
                    <Input variant="secondary" placeholder="输入当前原密码" />
                  </TextField>

                  <TextField fullWidth value={newUsername} onChange={setNewUsername}>
                    <Label className="text-xs">自定义新用户名（选填）</Label>
                    <Input variant="secondary" placeholder={`留空保持 ${currentUser?.username}`} />
                  </TextField>

                  <div className="space-y-1">
                    <TextField
                      fullWidth
                      type={showPw ? 'text' : 'password'}
                      value={password}
                      onChange={setPassword}
                    >
                      <div className="flex items-center justify-between">
                        <Label className="text-xs">自定义新密码（选填）</Label>
                        <Button
                          size="sm"
                          variant="ghost"
                          isIconOnly
                          aria-label={showPw ? '隐藏密码' : '显示密码'}
                          className="h-6 w-6 min-w-0"
                          onPress={() => setShowPw((v) => !v)}
                        >
                          {showPw ? <EyeOff className="w-3.5 h-3.5" /> : <Eye className="w-3.5 h-3.5" />}
                        </Button>
                      </div>
                      <Input variant="secondary" placeholder="至少 6 位字符，留空保持原密码" />
                    </TextField>
                  </div>
                </>
              ) : (
                <>
                  <TextField
                    fullWidth
                    isRequired
                    value={username}
                    onChange={setUsername}
                  >
                    <Label className="text-xs">玩家用户名（2~32 位字符）</Label>
                    <Input autoFocus variant="secondary" placeholder="如 Duelist_Yugi" />
                  </TextField>

                  <TextField
                    fullWidth
                    isRequired
                    type={showPw ? 'text' : 'password'}
                    value={password}
                    onChange={setPassword}
                  >
                    <div className="flex items-center justify-between">
                      <Label className="text-xs">登录密码</Label>
                      <Button
                        size="sm"
                        variant="ghost"
                        isIconOnly
                        aria-label={showPw ? '隐藏密码' : '显示密码'}
                        className="h-6 w-6 min-w-0"
                        onPress={() => setShowPw((v) => !v)}
                      >
                        {showPw ? <EyeOff className="w-3.5 h-3.5" /> : <Eye className="w-3.5 h-3.5" />}
                      </Button>
                    </div>
                    <Input variant="secondary" placeholder="至少 6 位字符" />
                    <Description className="text-[11px]">采用 Argon2id 单向慢哈希保护存储</Description>
                  </TextField>

                  {mode === 'register' && (
                    <TextField
                      fullWidth
                      isRequired
                      type={showPw ? 'text' : 'password'}
                      value={confirmPw}
                      onChange={setConfirmPw}
                    >
                      <Label className="text-xs">确认密码</Label>
                      <Input variant="secondary" placeholder="再次输入密码" />
                    </TextField>
                  )}
                </>
              )}
            </form>
          </Modal.Body>

          <Modal.Footer className="gap-2">
            <Button variant="tertiary" onPress={onClose}>
              取消
            </Button>
            <Button
              type="submit"
              form="portal-auth-form"
              variant="primary"
              isPending={loading}
              className="flex-1"
            >
              {loading && <Spinner color="current" size="sm" />}
              {loading
                ? '正在提交...'
                : mode === 'register'
                  ? '立即注册并登录'
                  : mode === 'login'
                    ? '安全登录'
                    : '保存账号设置'}
            </Button>
          </Modal.Footer>
        </Modal.Dialog>
      </Modal.Container>
    </Modal.Backdrop>
  );
}
