import { useEffect, useState } from 'react'
import type { FormEvent, JSX } from 'react'
import type { WsConnectionState } from '../protocol/wsClient'
import { ConnectionManager, type NetworkReachability } from './connectionManager'
import { DEFAULT_PORT } from './hostConfig'

const REACHABILITY_LABEL: Record<NetworkReachability, string> = {
  unknown: '未確認',
  checking: '確認中…',
  reachable: '到達可能',
  unreachable: '到達不可',
}

const REACHABILITY_COLOR: Record<NetworkReachability, string> = {
  unknown: 'bg-text-muted',
  checking: 'bg-warning',
  reachable: 'bg-success',
  unreachable: 'bg-danger',
}

const WS_STATE_LABEL: Record<WsConnectionState, string> = {
  idle: '未接続',
  connecting: '接続中…',
  open: '接続済み',
  reconnecting: '再接続中…',
  closed: '切断',
}

const WS_STATE_COLOR: Record<WsConnectionState, string> = {
  idle: 'bg-text-muted',
  connecting: 'bg-warning',
  open: 'bg-success',
  reconnecting: 'bg-warning',
  closed: 'bg-text-muted',
}

function StatusBadge({ label, colorClass }: { label: string; colorClass: string }): JSX.Element {
  return (
    <span className="flex items-center gap-2 text-sm text-text-muted">
      <span className={`h-2.5 w-2.5 rounded-full ${colorClass}`} aria-hidden="true" />
      {label}
    </span>
  )
}

function useStableInstance<T>(create: () => T): T {
  return useState(create)[0]
}

export function ConnectionTab(): JSX.Element {
  const manager = useStableInstance(() => new ConnectionManager())

  const [hostInput, setHostInput] = useState(manager.getHost())
  const [portInput, setPortInput] = useState(manager.getPort())
  const [reachability, setReachability] = useState<NetworkReachability>(manager.getReachability())
  const [wsState, setWsState] = useState<WsConnectionState>(manager.getWsState())

  useEffect(() => {
    const unsubscribers = [
      manager.onReachabilityChange(setReachability),
      manager.onWsStateChange(setWsState),
    ]
    manager.start()

    return () => {
      for (const unsubscribe of unsubscribers) unsubscribe()
      manager.stop()
    }
  }, [manager])

  const handleApply = (event: FormEvent): void => {
    event.preventDefault()
    const trimmedHost = hostInput.trim()
    if (trimmedHost === '') return
    manager.setHost(trimmedHost, portInput)
  }

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center gap-4 rounded-lg border border-border bg-surface-1 p-4">
        <StatusBadge
          label={`ネットワーク: ${REACHABILITY_LABEL[reachability]}`}
          colorClass={REACHABILITY_COLOR[reachability]}
        />
        <StatusBadge label={`WS: ${WS_STATE_LABEL[wsState]}`} colorClass={WS_STATE_COLOR[wsState]} />
      </div>

      <form
        onSubmit={handleApply}
        className="flex flex-col gap-3 rounded-lg border border-border bg-surface-1 p-4"
      >
        <p className="text-sm font-medium text-text-primary">接続先PC（同一LAN/Wi-Fi）</p>
        <div className="flex gap-2">
          <input
            type="text"
            value={hostInput}
            onChange={(event) => setHostInput(event.target.value)}
            placeholder="192.168.1.50 または mDNSホスト名"
            className="flex-1 rounded-md border border-border bg-surface-2 px-3 py-2 text-base text-text-primary"
          />
          <input
            type="number"
            value={portInput}
            onChange={(event) => setPortInput(Number(event.target.value) || DEFAULT_PORT)}
            className="w-24 rounded-md border border-border bg-surface-2 px-3 py-2 text-base text-text-primary"
          />
        </div>
        <button
          type="submit"
          className="self-start rounded-md bg-accent px-5 py-3 text-base font-medium text-white"
        >
          接続先を適用
        </button>
      </form>
    </div>
  )
}
