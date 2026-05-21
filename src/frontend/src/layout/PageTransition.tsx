import type { ReactNode } from "react";
import { motion } from "framer-motion";
import { motionTokens, pageVariants, reducedPageVariants } from "../theme/motion";

interface PageTransitionProps {
  children: ReactNode;
  reduceMotion: boolean;
}

export function PageTransition({ children, reduceMotion }: PageTransitionProps) {
  return (
    <motion.div
      className="page-transition"
      variants={reduceMotion ? reducedPageVariants : pageVariants}
      initial={false}
      animate="animate"
      transition={reduceMotion ? motionTokens.instant : motionTokens.pageCommit}
    >
      {children}
    </motion.div>
  );
}
