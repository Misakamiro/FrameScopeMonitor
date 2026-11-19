import { forwardRef, useMergeRefs, useDrawerContext, Tooltip as TooltipSurface, ModalOverlay as OverlaySurface, ModalContent as ModalSurface, DrawerContent as DrawerSurface, PopoverContent as PopoverSurface, MenuList as MenuSurface, Collapse as CollapseSurface,
  type TooltipProps, type ModalOverlayProps, type ModalContentProps, type DrawerContentProps, type PopoverContentProps, type MenuListProps, type CollapseProps } from '@chakra-ui/react';
import type { Variants } from 'framer-motion';
import { playSurface, useMotionPreferences, type MotionPreferences } from '@/lib/motion-preferences';
import {useLayoutEffect, useRef} from 'react';
import { jellyScales } from '@/lib/ui-effects';

export function motionVariants(p: MotionPreferences, axis: 'x'|'y' = 'y', direction = 1): Variants {
  const moving = !p.reduced && p.mode !== 'off';
  const jelly = !p.reduced && p.jelly && p.strength > 0;
  const scales = jellyScales(p.strength);
  return {
    exit: { opacity: moving && p.mode === 'fade' ? 0 : 1, x: moving && p.mode === 'slide' && axis === 'x' ? direction * 12 : 0, y: moving && p.mode === 'slide' && axis === 'y' ? direction * 12 : 0, scale: 1,
      transition: { duration: moving ? .15 : 0 }, transitionEnd: { visibility: 'hidden' } },
    enter: { opacity: 1, x: 0, y: 0, visibility: 'visible', scale: jelly ? [1, scales.peak, scales.compression, scales.settle, 1] : 1,
      transition: { duration: moving ? .18 : 0, ease: 'easeOut', scale: { duration: jelly ? .62 : 0 } } },
  };
}
function useSurface(axis: 'x'|'y' = 'y') { return { variants: motionVariants(useMotionPreferences(), axis), initial: 'exit', animate: 'enter', exit: 'exit' }; }
export const ModalContent = forwardRef<ModalContentProps,'section'>((props,ref) => <ModalSurface {...props} ref={ref} data-motion-owned="" motionProps={useSurface()}/>);
export const DrawerContent = forwardRef<DrawerContentProps,'div'>((props,ref) => {
  const {placement} = useDrawerContext();
  const variants = motionVariants(useMotionPreferences(), placement === 'top' || placement === 'bottom' ? 'y' : 'x', placement === 'left' || placement === 'top' ? -1 : 1);
  return <DrawerSurface {...props} ref={ref} data-motion-owned="" motionProps={{variants}}/>;
});
export const ModalOverlay = forwardRef<ModalOverlayProps,'div'>((props,ref) => {
  const p=useMotionPreferences(), duration=p.reduced||p.mode==='off'?0:.18;
  return <OverlaySurface {...props} ref={ref} motionProps={{variants:{enter:{opacity:1,transition:{duration}},exit:{opacity:0,transition:{duration}}}}}/>;
});
export const DrawerOverlay = ModalOverlay;
export const Tooltip = forwardRef<TooltipProps,'div'>((props,ref) => <TooltipSurface {...props} ref={ref} motionProps={useSurface()}/>);
export const PopoverContent = forwardRef<PopoverContentProps,'section'>((props,ref) => <PopoverSurface {...props} ref={ref} data-motion-owned="" variants={useSurface().variants}/>);
export const MenuList = forwardRef<MenuListProps,'div'>((props,ref) => <MenuSurface {...props} ref={ref} data-motion-owned="" motionProps={{variants:useSurface().variants}}/>);
export const Collapse = forwardRef<CollapseProps,'div'>((props,ref) => {
  const p=useMotionPreferences(), duration=p.reduced||p.mode==='off'?0:.18;
  const node=useRef<HTMLDivElement>(null), merged=useMergeRefs(node,ref);
  useLayoutEffect(()=>{if(props.in&&node.current){const animation=playSurface(node.current,true,'control');return()=>animation?.cancel();}},[props.in,p]);
  return <CollapseSurface {...props} ref={merged} data-motion-owned="" animateOpacity={p.mode==='fade'} transition={{enter:{duration},exit:{duration}}}/>;
});
