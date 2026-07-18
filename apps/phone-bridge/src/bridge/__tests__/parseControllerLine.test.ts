import { describe, expect, it } from 'vitest'
import { parseControllerLine, splitLines } from '../parseControllerLine'

describe('parseControllerLine', () => {
  it('parses a well-formed §4.1 JSON line', () => {
    const line =
      '{"event":"button_press","data":{"controller_id":"main","button_id":3,"timestamp":1773663861000}}'
    expect(parseControllerLine(line)).toEqual({
      controller_id: 'main',
      button_id: 3,
      timestamp: 1773663861000,
    })
  })

  it('trims surrounding whitespace / trailing CR', () => {
    const line = '  {"event":"button_press","data":{"controller_id":"sub","button_id":1,"timestamp":1}}\r'
    expect(parseControllerLine(line)).toEqual({ controller_id: 'sub', button_id: 1, timestamp: 1 })
  })

  it('returns null for empty/whitespace-only lines', () => {
    expect(parseControllerLine('')).toBeNull()
    expect(parseControllerLine('   ')).toBeNull()
  })

  it('returns null for broken JSON', () => {
    expect(parseControllerLine('{"event":"button_press",')).toBeNull()
  })

  it('returns null for well-formed JSON that does not match the §4.1 schema', () => {
    expect(parseControllerLine('{"event":"button_press","data":{"controller_id":"main"}}')).toBeNull()
    expect(parseControllerLine('{"foo":"bar"}')).toBeNull()
    expect(parseControllerLine('42')).toBeNull()
  })
})

describe('splitLines', () => {
  it('splits multiple newline-terminated lines from a single chunk', () => {
    const { lines, remainder } = splitLines('', 'a\nb\nc\n')
    expect(lines).toEqual(['a', 'b', 'c'])
    expect(remainder).toBe('')
  })

  it('buffers a partial trailing line for the next chunk', () => {
    const first = splitLines('', 'ab')
    expect(first.lines).toEqual([])
    expect(first.remainder).toBe('ab')

    const second = splitLines(first.remainder, 'cd\nef')
    expect(second.lines).toEqual(['abcd'])
    expect(second.remainder).toBe('ef')
  })

  it('reassembles a JSON line split across chunk boundaries', () => {
    const part1 = splitLines('', '{"event":"button_press","data":{"controller_id":"main",')
    expect(part1.lines).toEqual([])

    const part2 = splitLines(part1.remainder, '"button_id":2,"timestamp":9}}\n')
    expect(part2.lines).toEqual([
      '{"event":"button_press","data":{"controller_id":"main","button_id":2,"timestamp":9}}',
    ])
    expect(parseControllerLine(part2.lines[0])).toEqual({ controller_id: 'main', button_id: 2, timestamp: 9 })
  })

  it('handles multiple concatenated JSON lines in a single chunk', () => {
    const chunk =
      '{"event":"button_press","data":{"controller_id":"main","button_id":1,"timestamp":1}}\n' +
      '{"event":"button_press","data":{"controller_id":"sub","button_id":2,"timestamp":2}}\n'
    const { lines, remainder } = splitLines('', chunk)
    expect(lines).toHaveLength(2)
    expect(remainder).toBe('')
    expect(lines.map(parseControllerLine)).toEqual([
      { controller_id: 'main', button_id: 1, timestamp: 1 },
      { controller_id: 'sub', button_id: 2, timestamp: 2 },
    ])
  })
})
