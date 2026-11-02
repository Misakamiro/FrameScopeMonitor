import {useLayoutEffect} from 'react';
import {useColorMode} from '@chakra-ui/react';
import {useBackground} from '@/contexts/background-context';
import {useThemeColor} from '@/contexts/theme-color-context';
import {glassMaterial} from '@/lib/glass-material';
import {watchGlassBackground} from '@/lib/glass-background';
import {useLiquidGlassRefraction} from './special/liquid-glass-svg-filter';

export function UiSurfaceTokens() {
  const {colorMode}=useColorMode();
  const {liquidGlassEnabled:glass,liquidGlassBlur:blur,liquidGlassMode:mode,liquidGlassTransparency:transparency,backgroundMode,activeBgIndex,activePresetIndex,mrCustomColor,dynamicBgVideo}=useBackground();
  const {config,getActiveColor,getContrastTextColor}=useThemeColor();
  const {svgSupported}=useLiquidGlassRefraction(glass&&mode==='real');
  useLayoutEffect(()=>{
    const dark=colorMode==='dark';
    const settings={scheme:colorMode,enabled:glass,mode,transparency,blur};
    const material=glassMaterial(settings);
    document.documentElement.dataset.glassMaterial=!glass?'off':mode;
    document.documentElement.style.colorScheme=colorMode;
    const values={
      '--framescope-control-bg':material.background,
      '--framescope-real-glass-bg':glassMaterial({...settings,enabled:true,mode:'real'}).background,
      '--framescope-control-hover':dark?'rgba(255,255,255,.08)':'rgba(0,0,0,.07)',
      '--framescope-control-border':dark?'rgba(255,255,255,.2)':'rgba(40,48,62,.18)',
      '--framescope-control-fg':dark?'#f0f2f6':'#202633',
      '--framescope-panel-bg':material.background,
      '--framescope-surface-filter':material.filter,
      '--framescope-edge-filter':glass&&mode==='real'&&svgSupported?'url(#nexbox-liquid-glass-filter)':'none',
      '--framescope-primary':getActiveColor(),'--framescope-primary-hover':`color-mix(in srgb, ${getActiveColor()} 92%, black)`,'--framescope-primary-fg':getContrastTextColor(),
    };
    for(const [name,value] of Object.entries(values))document.documentElement.style.setProperty(name,value);
    return watchGlassBackground(settings,backgroundMode);
  },[colorMode,glass,blur,mode,transparency,backgroundMode,activeBgIndex,activePresetIndex,mrCustomColor,dynamicBgVideo,svgSupported,config,getActiveColor,getContrastTextColor]);
  return null;
}
