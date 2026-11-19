// Shared presentation extracted from FeatureWorkbench for the public OSD editor.
import type { ReactNode } from 'react';
import { Box, Button, Flex, Heading, HStack, Text } from '@chakra-ui/react';
import { ArrowLeft } from 'lucide-react';
import { useAdaptiveTextColor } from '@/hooks/use-adaptive-text-color';

export function OverlayWorkbench({id,title,description,actions,children,onBack,backLabel}:{id:string;title:string;description:string;actions?:ReactNode;children:ReactNode;onBack:()=>void;backLabel:string}){
  const colors=useAdaptiveTextColor();
  return <Box id={id} pt={8} color={colors.text}>
    <Flex mb={5} justify="space-between" align="start" gap={4} wrap="wrap">
      <Box><Button size="sm" variant="ghost" mb={3} leftIcon={<ArrowLeft size={16}/>} onClick={onBack}>{backLabel}</Button><Heading size="lg" textShadow={colors.shadow}>{title}</Heading><Text mt={2} fontSize="sm" opacity={.7}>{description}</Text></Box>
      {actions&&<HStack pt={3} wrap="wrap">{actions}</HStack>}
    </Flex>{children}
  </Box>;
}
