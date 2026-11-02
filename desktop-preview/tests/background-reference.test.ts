import {describe,it,expect} from 'vitest';
import {createBackgroundReferences} from '../src/lib/background-reference';
import {composeLuminance,foregroundForSurface} from '../src/lib/glass-material';

describe('session background reference',()=>{
  it('freezes the first valid frame across bright and dark animation frames',()=>{
    const cache=createBackgroundReferences();let reads=0;
    const branches=[.03,.4,.03,.4].map(value=>foregroundForSurface(composeLuminance(cache.video('a',.97,()=>{reads++;return value;}),255,.25)));
    expect(branches).toEqual(['light','light','light','light']);expect(reads).toBe(1);
  });
  it('waits for readiness, isolates sources and reuses an earlier video',()=>{
    const cache=createBackgroundReferences();expect(cache.video('a',.97)).toBe(.97);
    expect(cache.video('a',.97,()=>.03)).toBe(.03);
    expect(cache.video('b',.97,()=>.9)).toBe(.9);
    expect(cache.video('a',.97,()=>.9)).toBe(.03);
    expect(cache.video('a',.003)).toBe(.03);
  });
  it('keeps capture failure deterministic without retrying or reusing another source',()=>{
    const cache=createBackgroundReferences();cache.video('a',.97,()=>.9);
    expect(cache.video('failed',.003,()=>{throw new Error('unreadable');})).toBe(.003);
    expect(cache.video('failed',.97,()=>.9)).toBe(.97);
  });
});
