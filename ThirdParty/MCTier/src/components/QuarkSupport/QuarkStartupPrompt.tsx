import { useEffect, useState } from 'react';
import { Button, Modal } from 'antd';
import {
  getQuarkSupportSnapshot,
  loadQuarkSupport,
  useQuarkSupport,
} from '../../services/quarkSupport';
import { getLanguage, tl } from '../../i18n';
import { QuarkSupportWindow } from './QuarkSupport';

export const OPEN_QUARK_SUPPORT = 'mctier-open-quark-support';

/** Keep the invitation pending while a higher-priority version prompt is active. */
export function QuarkStartupPrompt({ automatic = true, blocked, onBlockingChange }: { automatic?: boolean; blocked: boolean; onBlockingChange: (blocking: boolean) => void }) {
  const state = useQuarkSupport();
  const [resolved, setResolved] = useState(!automatic);
  const [handled, setHandled] = useState(!automatic);
  const [supportOpen, setSupportOpen] = useState(false);
  useEffect(() => {
    let active = true;
    if (automatic) void loadQuarkSupport().catch(() => {}).finally(() => {
      if (active) {
        setResolved(true);
        if (getQuarkSupportSnapshot().loggedIn) setHandled(true);
      }
    });
    const open = () => {
      setHandled(true);
      setSupportOpen(true);
    };
    window.addEventListener(OPEN_QUARK_SUPPORT, open);
    return () => {
      active = false;
      window.removeEventListener(OPEN_QUARK_SUPPORT, open);
    };
  }, []);
  const open = resolved && state.ready && !state.loggedIn && !handled;
  useEffect(() => { onBlockingChange(!resolved || open || supportOpen); }, [resolved, open, supportOpen, onBlockingChange]);
  return (
    <>
      <Modal
        open={open && !blocked}
        title={tl('免费持续支持 BCCL虚拟局域网联机', 'Support BCCL虚拟局域网联机 for free')}
        centered
        width={420}
        zIndex={900}
        maskClosable={false}
        closable={false}
        onCancel={() => setHandled(true)}
        footer={[
          <Button
            key="later"
            style={{ color: '#686868', background: '#ededed', borderColor: '#d4d4d4' }}
            onClick={() => setHandled(true)}
          >
            {tl('下次一定', 'Maybe next time')}
          </Button>,
          <Button
            key="support"
            type="primary"
            style={{ background: '#27823b', borderColor: '#27823b', color: '#fff' }}
            onClick={() => {
              setHandled(true);
              setSupportOpen(true);
            }}
          >
            {tl('支持一下', 'Support BCCL虚拟局域网联机')}
          </Button>,
        ]}
      >
        <p style={{ textIndent: '2em' }}>
          {getLanguage() === 'en' ? (
            <>Please sign in to <strong className="quark-startup-highlight">Quark</strong> through BCCL虚拟局域网联机. Daily saves can bring in <strong className="quark-startup-highlight">a few tenths of a yuan</strong> to help fund ongoing updates.</>
          ) : (
            <>希望您能在BCCL虚拟局域网联机上登录一下<strong className="quark-startup-highlight">夸克账号</strong>，这样就能每天为BCCL虚拟局域网联机带来<strong className="quark-startup-highlight">几角钱的转存费</strong>以维持软件的更新迭代。</>
          )}
        </p>
        <p style={{ textIndent: '2em' }}>
          {getLanguage() === 'en' ? (
            <>You can keep supporting BCCL虚拟局域网联机 development through Quark Drive <strong className="quark-startup-highlight">without spending anything</strong>. Quark covers the costs.</>
          ) : (
            <><strong className="quark-startup-highlight">您不用掏一分钱</strong>，就能<strong className="quark-startup-highlight">利用夸克网盘来持续赞助BCCL虚拟局域网联机</strong>的开发工作，所有的费用都由夸克出。</>
          )}
        </p>
        <p style={{ textIndent: '2em' }}>
          {getLanguage() === 'en' ? (
            <>Your <strong className="quark-startup-highlight">Quark account</strong> credentials are stored locally on your computer, and you can sign out at any time.</>
          ) : (
            <>登上的<strong className="quark-startup-highlight">夸克账号</strong>凭证完全保存在您电脑本地，并且可以随时退出登录。</>
          )}
        </p>
        <p style={{ textIndent: '2em' }}>
          {getLanguage() === 'en' ? (
            <>We hope you will support BCCL虚拟局域网联机. <strong className="quark-startup-highlight">Your support is vital to its continued development</strong>!</>
          ) : (
            <>希望您能支持一下，<strong className="quark-startup-highlight">这对BCCL虚拟局域网联机的发展至关重要</strong>！</>
          )}
        </p>
      </Modal>
      <QuarkSupportWindow
        open={supportOpen && !blocked}
        onClose={() => setSupportOpen(false)}
      />
    </>
  );
}
