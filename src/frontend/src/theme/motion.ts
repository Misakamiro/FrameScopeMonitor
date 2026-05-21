import type { Transition, Variants } from "framer-motion";

export const motionTokens = {
  springStandard: {
    type: "spring",
    stiffness: 320,
    damping: 30,
    mass: 0.8,
  } satisfies Transition,
  springRoute: {
    type: "spring",
    stiffness: 220,
    damping: 26,
    mass: 0.9,
  } satisfies Transition,
  springPress: {
    type: "spring",
    stiffness: 520,
    damping: 34,
    mass: 0.7,
  } satisfies Transition,
  crossfade: {
    duration: 0.18,
    ease: [0.22, 1, 0.36, 1],
  } satisfies Transition,
  pageCommit: {
    duration: 0.01,
    ease: "linear",
  } satisfies Transition,
  instant: {
    duration: 0,
  } satisfies Transition,
};

export const pageVariants: Variants = {
  animate: { opacity: 1, y: 0, scale: 1 },
};

export const listItemVariants: Variants = {
  initial: { opacity: 0, y: 10 },
  animate: { opacity: 1, y: 0 },
};

export const reducedPageVariants: Variants = {
  animate: { opacity: 1 },
};
