import { useAppStartup } from "@/contexts/app-startup-context";
import { useThemeColor } from "@/contexts/theme-color-context";
import { Box, Text, VStack } from "@chakra-ui/react";

// FrameScope-brand minimal splash: icon + wordmark + thin progress bar with a
// pure-CSS ambient glow. Replaces the splash that was copied byte-for-byte
// from NexBox (WebGL neon lines + "新境盒" wordmark); the startup/progress
// driver in app-startup-context is unchanged.
export function SplashScreen() {
  const { startupProgress } = useAppStartup();
  const { getActiveColor } = useThemeColor();
  const primaryColor = getActiveColor();
  return (
    <Box
      w="100vw"
      h="100vh"
      className="framescope-splash"
      position="fixed"
      top="0"
      left="0"
      zIndex="9999"
      data-tauri-drag-region
      opacity={startupProgress >= 100 ? 0 : 1}
      transition="opacity 0.4s ease-out"
      willChange="opacity"
    >
      <Box className="framescope-splash-glow" />

      <VStack
        position="absolute"
        top="50%"
        left="50%"
        transform="translate(-50%, -50%)"
        spacing={5}
        zIndex="10"
        data-tauri-drag-region
      >
        <Box
          as="img"
          src="/logo/framescope.png"
          alt="FrameScope"
          w="96px"
          h="96px"
          borderRadius="24px"
          boxShadow="0 18px 48px rgba(0, 0, 0, 0.55)"
          draggable={false}
          className="framescope-splash-icon"
        />
        <Text
          className="framescope-splash-title"
          fontSize="26px"
          fontWeight="semibold"
          color="#e8edf4"
          letterSpacing="0.12em"
        >
          FrameScope
        </Text>
      </VStack>

      <Box
        position="absolute"
        bottom="25%"
        left="50%"
        transform="translateX(-50%)"
        w="40%"
        maxW="180px"
        zIndex="10"
        pointerEvents="none"
      >
        <Box bg="rgba(255,255,255,0.14)" h="2px" borderRadius="full" overflow="hidden">
          <Box
            h="100%"
            bg={primaryColor}
            w={`${startupProgress}%`}
            transition="width 200ms ease-out"
            willChange="width"
          />
        </Box>
      </Box>
    </Box>
  );
}
