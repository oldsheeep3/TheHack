import { useState } from 'react'
import type { JSX } from 'react'
import { ConnectionTab } from './net/ConnectionTab'

type Tab = 'operate' | 'settings'

const TABS: { id: Tab; label: string }[] = [
  { id: 'operate', label: '操作/接続モード' },
  { id: 'settings', label: '設定モード' },
]

function SettingsPlaceholder(): JSX.Element {
  return (
    <div className="rounded-lg border border-border bg-surface-1 p-4 text-sm text-text-muted">
      設定モード（ソース/2系統ME/マルチビュー/出力/モジュール/Picoネットワーク編集）は後続タスクで実装されます。
    </div>
  )
}

function App(): JSX.Element {
  const [activeTab, setActiveTab] = useState<Tab>('operate')

  return (
    <div className="flex min-h-screen flex-col bg-surface-0 text-text-primary">
      <header className="border-b border-border px-4 py-3">
        <h1 className="text-lg font-semibold">Phone Bridge</h1>
      </header>

      <nav className="flex border-b border-border" role="tablist" aria-label="モード切替">
        {TABS.map((tab) => (
          <button
            key={tab.id}
            type="button"
            role="tab"
            aria-selected={activeTab === tab.id}
            onClick={() => setActiveTab(tab.id)}
            className={`flex-1 px-4 py-4 text-base font-medium transition-colors ${
              activeTab === tab.id
                ? 'border-b-2 border-accent bg-accent-muted text-text-primary'
                : 'text-text-muted hover:text-text-primary'
            }`}
          >
            {tab.label}
          </button>
        ))}
      </nav>

      <main className="flex-1 p-4" role="tabpanel">
        {activeTab === 'operate' ? <ConnectionTab /> : <SettingsPlaceholder />}
      </main>
    </div>
  )
}

export default App
