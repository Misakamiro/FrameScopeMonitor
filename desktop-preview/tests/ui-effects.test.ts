import { describe, expect, it } from 'vitest';
import { effectPercentage, realGlassBackground, glassTextUsesDarkBranch, jellyScales } from '../src/lib/ui-effects';

describe('user-controlled effects keep the existing default',()=>{
  it('normalizes missing, invalid and out-of-range settings',()=>{
    expect(effectPercentage(undefined,100)).toBe(100);
    expect(effectPercentage(Number.NaN,100)).toBe(100);
    expect(effectPercentage(-10,100)).toBe(0);
    expect(effectPercentage(250,200)).toBe(200);
    expect(effectPercentage(38.7,100)).toBe(39);
  });
  it('changes only the material tint, with a completely unchanged default',()=>{
    expect(realGlassBackground('light',100)).toBe('transparent');
    expect(realGlassBackground('dark',100)).toBe('transparent');
    expect(realGlassBackground('light',0)).toBe('rgba(255,255,255,1)');
    expect(realGlassBackground('dark',50)).toBe('rgba(17,17,17,0.5)');
  });
  it('selects the existing readable branch for the actual MR material',()=>{
    expect(glassTextUsesDarkBranch('light','mr',true,'real',100)).toBe(true);
    expect(glassTextUsesDarkBranch('light','mr',true,'real',0)).toBe(false);
    expect(glassTextUsesDarkBranch('light','mr',true,'normal',0)).toBe(true);
    expect(glassTextUsesDarkBranch('light','mr',false,'real',100)).toBe(false);
    expect(glassTextUsesDarkBranch('light','preset',true,'real',100)).toBe(false);
    expect(glassTextUsesDarkBranch('dark','mr',true,'real',0)).toBe(true);
  });
  it('scales original deviations while leaving timing and placement out of the calculation',()=>{
    expect(jellyScales(100)).toEqual({peak:1.05,compression:0.97,settle:1.02,small:0.99,subtlePeak:1.06,subtleSettle:1.015});
    expect(Object.values(jellyScales(0))).toEqual([1,1,1,1,1,1]);
    expect(jellyScales(200).peak).toBe(1.1);
    expect(jellyScales(200).compression).toBe(0.94);
  });
});
