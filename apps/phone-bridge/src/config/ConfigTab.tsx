import { useEffect, useMemo, useState } from 'react'
import type { JSX } from 'react'
import { ApiClient } from '../protocol/apiClient'
import type { PipSettings, SourceInfo, SourceStatus, TallyState } from '../protocol/types'
import { debounce } from './debounce'
import { PipEditor } from './PipEditor'
import { LocalStoragePresetStore, type ScenePreset } from './presets'

const SOURCES_POLL_MS = 3000
const TALLY_POLL_MS = 300
const CONFIG_SEND_DEBOUNCE_MS = 150

const STATUS_LABEL: Record<SourceStatus, string> = {
  connected: '接続済み',
  disconnected: '未接続',
  error: 'エラー',
}

const STATUS_COLOR: Record<SourceStatus, string> = {
  connected: 'bg-success',
  disconnected: 'bg-text-muted',
  error: 'bg-danger',
}

const DEFAULT_PIP: PipSettings = {
  enabled: true,
  x_position: 0,
  y_position: 0,
  width: 480,
  height: 270,
  opacity: 1,
  z_order: 1,
  crop: null,
}

function useStableInstance<T>(create: () => T): T {
  return useState(create)[0]
}

export function ConfigTab(): JSX.Element {
  const apiClient = useStableInstance(() => new ApiClient())
  const presetStore = useStableInstance(() => new LocalStoragePresetStore())

  const [sources, setSources] = useState<SourceInfo[]>([])
  const [tally, setTally] = useState<TallyState | null>(null)
  const [selectedChannel, setSelectedChannel] = useState<number | null>(null)
  const [pipByChannel, setPipByChannel] = useState<Record<number, PipSettings>>({})
  const [presetNames, setPresetNames] = useState<string[]>(() => presetStore.list())
  const [newPresetName, setNewPresetName] = useState('')
  const [lastError, setLastError] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    const poll = (): void => {
      apiClient
        .getSources()
        .then((result) => {
          if (!cancelled) setSources(result)
        })
        .catch((error: unknown) => {
          if (!cancelled) setLastError(error instanceof Error ? error.message : String(error))
        })
    }
    poll()
    const timer = setInterval(poll, SOURCES_POLL_MS)
    return () => {
      cancelled = true
      clearInterval(timer)
    }
  }, [apiClient])

  useEffect(() => {
    let cancelled = false
    const poll = (): void => {
      apiClient
        .getTallyState()
        .then((result) => {
          if (!cancelled) setTally(result)
        })
        .catch(() => {
          // Tally is a best-effort overlay; a poll failure just leaves the last known state.
        })
    }
    poll()
    const timer = setInterval(poll, TALLY_POLL_MS)
    return () => {
      cancelled = true
      clearInterval(timer)
    }
  }, [apiClient])

  const sendConfig = useStableInstance(() =>
    debounce((channel: number, source: SourceInfo, pip: PipSettings) => {
      apiClient
        .updateConfig({
          target_channel: channel,
          source_type: source.protocol,
          source_url: null,
          pip_settings: pip,
        })
        .catch((error: unknown) => setLastError(error instanceof Error ? error.message : String(error)))
    }, CONFIG_SEND_DEBOUNCE_MS),
  )

  const selectedSource = useMemo(
    () => sources.find((source) => source.channel === selectedChannel) ?? null,
    [sources, selectedChannel],
  )
  const selectedPip = selectedChannel !== null ? (pipByChannel[selectedChannel] ?? DEFAULT_PIP) : null

  const handleSelectChannel = (channel: number): void => {
    setSelectedChannel(channel)
    setPipByChannel((prev) => (prev[channel] ? prev : { ...prev, [channel]: DEFAULT_PIP }))
  }

  const handlePipChange = (pip: PipSettings): void => {
    if (selectedChannel === null || !selectedSource) return
    setPipByChannel((prev) => ({ ...prev, [selectedChannel]: pip }))
    sendConfig(selectedChannel, selectedSource, pip)
  }

  const handleSavePreset = (): void => {
    const name = newPresetName.trim()
    if (!name || Object.keys(pipByChannel).length === 0) return
    presetStore.save({ name, channels: pipByChannel })
    setPresetNames(presetStore.list())
    setNewPresetName('')
  }

  const handleLoadPreset = (name: string): void => {
    const preset: ScenePreset | null = presetStore.load(name)
    if (!preset) return
    setPipByChannel(preset.channels)
    for (const [channelKey, pip] of Object.entries(preset.channels)) {
      const channel = Number(channelKey)
      const source = sources.find((candidate) => candidate.channel === channel)
      if (source) sendConfig(channel, source, pip)
    }
  }

  const handleRemovePreset = (name: string): void => {
    presetStore.remove(name)
    setPresetNames(presetStore.list())
  }

  return (
    <div className="flex flex-col gap-4">
      {lastError && (
        <p className="rounded-md border border-danger/40 bg-danger/10 px-3 py-2 text-sm text-danger">
          {lastError}
        </p>
      )}

      <div className="rounded-lg border border-border bg-surface-1 p-4">
        <p className="mb-2 text-sm font-medium text-text-primary">入力ソース</p>
        {sources.length === 0 ? (
          <p className="text-sm text-text-muted">ソースを取得しています…</p>
        ) : (
          <ul className="flex flex-col gap-2">
            {sources.map((source) => {
              const isPgm = tally?.active_pgm.includes(source.channel) ?? false
              const isPvw = tally?.active_pvw.includes(source.channel) ?? false
              return (
                <li key={source.channel}>
                  <button
                    type="button"
                    onClick={() => handleSelectChannel(source.channel)}
                    className={`flex w-full items-center justify-between gap-3 rounded-md px-4 py-3 text-left text-base transition-colors ${
                      selectedChannel === source.channel
                        ? 'bg-accent-muted ring-1 ring-accent'
                        : 'bg-surface-2 hover:bg-surface-2/70'
                    }`}
                  >
                    <span className="flex items-center gap-2">
                      <span
                        className={`h-2.5 w-2.5 rounded-full ${STATUS_COLOR[source.status]}`}
                        aria-hidden="true"
                      />
                      <span className="font-medium text-text-primary">
                        CH{source.channel} {source.name}
                      </span>
                      <span className="text-sm text-text-muted">
                        {source.protocol} / {STATUS_LABEL[source.status]}
                      </span>
                    </span>
                    <span className="flex gap-1">
                      {isPgm && (
                        <span className="rounded bg-danger px-2 py-0.5 text-xs font-semibold text-white">
                          PGM
                        </span>
                      )}
                      {isPvw && (
                        <span className="rounded bg-success px-2 py-0.5 text-xs font-semibold text-black">
                          PVW
                        </span>
                      )}
                    </span>
                  </button>
                </li>
              )
            })}
          </ul>
        )}
      </div>

      {selectedSource && selectedPip && (
        <div className="rounded-lg border border-border bg-surface-1 p-4">
          <p className="mb-3 text-sm font-medium text-text-primary">
            CH{selectedSource.channel} PiPレイアウト
          </p>
          <PipEditor pip={selectedPip} onChange={handlePipChange} />
        </div>
      )}

      <div className="rounded-lg border border-border bg-surface-1 p-4">
        <p className="mb-2 text-sm font-medium text-text-primary">シーンプリセット</p>
        <div className="flex gap-2">
          <input
            type="text"
            value={newPresetName}
            onChange={(event) => setNewPresetName(event.target.value)}
            placeholder="プリセット名"
            className="flex-1 rounded-md border border-border bg-surface-2 px-3 py-2 text-base text-text-primary"
          />
          <button
            type="button"
            onClick={handleSavePreset}
            disabled={!newPresetName.trim() || Object.keys(pipByChannel).length === 0}
            className="rounded-md bg-accent px-4 py-2 text-base font-medium text-white disabled:cursor-not-allowed disabled:opacity-40"
          >
            保存
          </button>
        </div>

        {presetNames.length === 0 ? (
          <p className="mt-3 text-sm text-text-muted">保存済みプリセットはありません。</p>
        ) : (
          <ul className="mt-3 flex flex-col gap-2">
            {presetNames.map((name) => (
              <li key={name} className="flex items-center justify-between gap-2 rounded-md bg-surface-2 px-3 py-2">
                <span className="text-base text-text-primary">{name}</span>
                <span className="flex gap-2">
                  <button
                    type="button"
                    onClick={() => handleLoadPreset(name)}
                    className="rounded-md bg-accent-muted px-3 py-2 text-sm font-medium text-text-primary"
                  >
                    読込
                  </button>
                  <button
                    type="button"
                    onClick={() => handleRemovePreset(name)}
                    className="rounded-md bg-danger/20 px-3 py-2 text-sm font-medium text-danger"
                  >
                    削除
                  </button>
                </span>
              </li>
            ))}
          </ul>
        )}
      </div>
    </div>
  )
}
