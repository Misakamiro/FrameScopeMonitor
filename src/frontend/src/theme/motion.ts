const standardOut = [0.2, 0, 0, 1] as const;
const settleOut = [0.22, 1, 0.36, 1] as const;
const pressOut = [0.2, 0, 0.2, 1] as const;

type MotionTransition = {
  duration: number;
  ease?: readonly number[] | "linear";
};

type MotionVariants = Record<string, Record<string, number>>;

export const motionTokens = {
  instant: {
    duration: 0,
  } satisfies MotionTransition,
  micro: {
    duration: 0.09,
    ease: standardOut,
  } satisfies MotionTransition,
  state: {
    duration: 0.14,
    ease: standardOut,
  } satisfies MotionTransition,
  content: {
    duration: 0.18,
    ease: settleOut,
  } satisfies MotionTransition,
  navCommit: {
    duration: 0.04,
    ease: "linear",
  } satisfies MotionTransition,
  press: {
    duration: 0.08,
    ease: pressOut,
  } satisfies MotionTransition,
};

export const pageVariants: MotionVariants = {
  animate: { opacity: 1 },
};

export const reducedPageVariants: MotionVariants = {
  animate: { opacity: 1 },
};
