export function effectPercentage(value: unknown, maximum: number): number {
  return typeof value === 'number' && Number.isFinite(value)
    ? Math.max(0,Math.min(maximum,Math.round(value))) : 100;
}

export function realGlassBackground(scheme: 'light'|'dark', transparency: number): string {
  const value=effectPercentage(transparency,100);
  if(value===100)return 'transparent';
  return `rgba(${scheme==='dark'?'17,17,17':'255,255,255'},${(100-value)/100})`;
}

export function glassTextUsesDarkBranch(scheme: string, background: string, glass: boolean, mode: string, transparency=100): boolean {
  if(scheme==='dark')return true;
  if(background!=='mr'||!glass)return false;
  if(mode!=='real')return true;
  const alpha=1-effectPercentage(transparency,100)/100;
  const channel=alpha+(1-alpha)*0.05;
  const luminance=channel<=0.04045?channel/12.92:((channel+0.055)/1.055)**2.4;
  return 1.05/(luminance+0.05)>(luminance+0.05)/0.05;
}

export function jellyScales(strength: number) {
  const factor=effectPercentage(strength,200)/100;
  const scale=(deviation:number)=>Number((1+deviation*factor).toFixed(6));
  return {peak:scale(.05),compression:scale(-.03),settle:scale(.02),small:scale(-.01),subtlePeak:scale(.06),subtleSettle:scale(.015)};
}
