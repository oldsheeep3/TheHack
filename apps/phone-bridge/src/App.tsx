import { useState } from 'react'
import type { JSX } from 'react'
import { RelayTab } from './bridge/RelayTab'
import { ConfigTab } from './config/ConfigTab'

type Tab = 'relay' | 'config'

const TABS: { id: Tab; label: string }[] = [
  { id: 'relay', label: '中継モード' },
  { id: 'config', label: '設定モード' },
]

function App(): JSX.Element {
  const [activeTab, setActiveTab] = useState<Tab>('relay')

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
        {activeTab === 'relay' ? <RelayTab /> : <ConfigTab />}
      </main>
    </div>
  )
}

export default App
