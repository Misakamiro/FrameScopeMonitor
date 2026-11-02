import {describe,it,expect} from 'vitest';
import {glassMaterial,foregroundForSurface,composeLuminance} from '../src/lib/glass-material';

describe('material and foreground contract',()=>{
  it('composes a white tint in sRGB before evaluating contrast',()=>{
    expect(composeLuminance(.03,255,.25)).toBeLessThan(.179);
    expect(foregroundForSurface(composeLuminance(.03,255,.25))).toBe('light');
  });
  it('keeps transparent controls and panels clear at 100 percent',()=>{
    for(const scheme of ['light','dark'] as const){
      const m=glassMaterial({scheme,enabled:true,mode:'real',transparency:100,blur:16});
      expect(m.background).toBe('transparent');expect(m.filter).toBe('none');expect(m.alpha).toBe(0);
    }
  });
  it('uses the same tint and blur for ordinary surfaces',()=>{
    const m=glassMaterial({scheme:'light',enabled:true,mode:'normal',transparency:100,blur:7});
    expect(m.alpha).toBe(.25);expect(m.filter).toContain('blur(7px)');
  });
  it('preserves opacity endpoints and switches foreground with composed background',()=>{
    for(const scheme of ['light','dark'] as const){
      const solid=glassMaterial({scheme,enabled:true,mode:'real',transparency:0,blur:1});
      expect(solid.alpha).toBe(1);
      expect(foregroundForSurface(scheme==='light'?1:0)).toBe(scheme==='light'?'dark':'light');
    }
    expect(foregroundForSurface(.9)).toBe('dark');expect(foregroundForSurface(.02)).toBe('light');
    expect(foregroundForSurface(.18,'light')).toBe('light');
    expect(foregroundForSurface(.18,'dark')).toBe('dark');
  });
});
