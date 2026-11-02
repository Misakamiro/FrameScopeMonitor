import { describe, expect, it, vi } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { ChakraProvider, DarkMode } from '@chakra-ui/react';
import { useGlassTextColor } from '../src/hooks/use-adaptive-text-color';

const background = vi.hoisted(()=>({backgroundMode:'mr',liquidGlassEnabled:true,liquidGlassMode:'real'}));
vi.mock('@/contexts/background-context',()=>({useBackground:()=>background}));

function Probe() {return <span>{useGlassTextColor('light-foreground','dark-foreground')}</span>;}

describe('foreground follows the existing surface branch',()=>{
  it.each([
    ['mr',true,'normal',false,'dark-foreground'],
    ['mr',true,'real',false,'dark-foreground'],
    ['mr',false,'real',false,'light-foreground'],
    ['preset',true,'real',false,'light-foreground'],
    ['image',true,'normal',false,'light-foreground'],
    ['preset',false,'normal',true,'dark-foreground'],
  ] as const)('%s glass=%s mode=%s dark=%s', (mode,glass,material,dark,expected)=>{
    Object.assign(background,{backgroundMode:mode,liquidGlassEnabled:glass,liquidGlassMode:material});
    const markup=renderToStaticMarkup(<ChakraProvider>{dark?<DarkMode><Probe/></DarkMode>:<Probe/>}</ChakraProvider>);
    expect(markup).toContain(`<span>${expected}</span>`);
  });
});
