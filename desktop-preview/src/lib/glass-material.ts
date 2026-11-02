export type ForegroundBranch = 'light' | 'dark';
export interface MaterialSettings {
  scheme:'light'|'dark'; enabled:boolean; mode:'normal'|'real'; transparency:number; blur:number;
}

export function glassMaterial(settings:MaterialSettings) {
  const {scheme,enabled,mode}=settings;
  const transparency=Number.isFinite(settings.transparency)?Math.max(0,Math.min(100,settings.transparency)):100;
  const alpha=!enabled?1:mode==='real'?(100-transparency)/100:.25;
  const channel=scheme==='dark'?17:255;
  return {alpha,channel,background:alpha===0?'transparent':`rgba(${channel},${channel},${channel},${alpha})`,
    filter:enabled&&mode==='normal'?`blur(${Math.max(0,settings.blur)}px) saturate(1.3)`:'none'};
}

export function linearChannel(value:number) {
  return value<=.04045?value/12.92:((value+.055)/1.055)**2.4;
}

export function composeLuminance(background:number,channel:number,alpha:number) {
  const srgb=background<=.0031308?12.92*background:1.055*background**(1/2.4)-.055;
  return linearChannel(srgb*(1-alpha)+channel/255*alpha);
}

export function foregroundForSurface(luminance:number,previous?:ForegroundBranch):ForegroundBranch {
  // Stable band around the equal black/white contrast point.
  if(previous&&luminance>=.15&&luminance<=.21)return previous;
  return luminance>.179?'dark':'light';
}

export function foregroundTokens(branch:ForegroundBranch) {
  return branch==='dark'
    ?{foreground:'#17202b',muted:'#394553',border:'rgba(24,35,48,.3)',hover:'rgba(0,0,0,.07)'}
    :{foreground:'#f5f7fa',muted:'#d4dbe4',border:'rgba(255,255,255,.3)',hover:'rgba(255,255,255,.08)'};
}
