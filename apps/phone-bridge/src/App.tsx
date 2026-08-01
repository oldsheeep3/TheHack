import { useState } from 'react'
import type { JSX } from 'react'
import { ConfigTab } from './config/ConfigTab'
import { ConnectionTab } from './net/ConnectionTab'

type Tab = 'connection' | 'settings'

// This client is settings-only: it configures the PC, it does not run the show. TAKE and tally live on
// the PC console and the module panel, which is where an operator's hands already are.
const TABS: { id: Tab; label: string }[] = [
  { id: 'connection', label: '接続先' },
  { id: 'settings', label: '設定' },
]

function App(): JSX.Element {
  const [activeTab, setActiveTab] = useState<Tab>('connection')

  return (
    <div className="flex min-h-screen flex-col bg-surface-0 text-text-primary">
      <header className="border-b border-border px-4 py-3">
        <h1 className="text-lg font-semibold">Switcher 設定</h1>
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
        {activeTab === 'connection' ? <ConnectionTab /> : <ConfigTab />}
      </main>
    </div>
  )
}

export default App
