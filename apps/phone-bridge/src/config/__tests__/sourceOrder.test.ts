import { describe, expect, it } from 'vitest'
import { mergeOrder, moveIdInOrder, sortSourcesByOrder } from '../sourceOrder'

describe('sortSourcesByOrder', () => {
  it('sorts known ids by the given order', () => {
    const sources = [{ id: 'b' }, { id: 'a' }, { id: 'c' }]
    expect(sortSourcesByOrder(sources, ['c', 'a', 'b']).map((s) => s.id)).toEqual(['c', 'a', 'b'])
  })

  it('appends unranked ids after ranked ones, preserving their relative order', () => {
    const sources = [{ id: 'z' }, { id: 'a' }, { id: 'y' }]
    expect(sortSourcesByOrder(sources, ['a']).map((s) => s.id)).toEqual(['a', 'z', 'y'])
  })

  it('treats null ids as unranked', () => {
    const sources = [{ id: null }, { id: 'a' }]
    expect(sortSourcesByOrder(sources, ['a']).map((s) => s.id)).toEqual(['a', null])
  })
})

describe('moveIdInOrder', () => {
  it('moves an id one slot up', () => {
    expect(moveIdInOrder(['a', 'b', 'c'], 'b', 'up')).toEqual(['b', 'a', 'c'])
  })

  it('moves an id one slot down', () => {
    expect(moveIdInOrder(['a', 'b', 'c'], 'b', 'down')).toEqual(['a', 'c', 'b'])
  })

  it('is a no-op at the boundary', () => {
    expect(moveIdInOrder(['a', 'b'], 'a', 'up')).toEqual(['a', 'b'])
    expect(moveIdInOrder(['a', 'b'], 'b', 'down')).toEqual(['a', 'b'])
  })

  it('is a no-op for an unknown id', () => {
    expect(moveIdInOrder(['a', 'b'], 'z', 'up')).toEqual(['a', 'b'])
  })
})

describe('mergeOrder', () => {
  it('appends newly seen ids not already present', () => {
    expect(mergeOrder(['a'], ['a', 'b', 'c'])).toEqual(['a', 'b', 'c'])
  })

  it('leaves the order untouched when nothing is new', () => {
    expect(mergeOrder(['a', 'b'], ['a'])).toEqual(['a', 'b'])
  })
})
