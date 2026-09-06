import { Box, Spinner, Text, VStack, useColorModeValue } from "@chakra-ui/react";
import { useState } from "react";
import { useTranslation } from "react-i18next";

export default function MoodPage() {
  const [loading, setLoading] = useState(true);
  const textColor = useColorModeValue("white", "white");
  const { t } = useTranslation();

  return (
    <Box
      position="absolute"
      top={0}
      left={0}
      right={0}
      bottom={0}
      p={4}
    >
      <VStack
        position="absolute"
        top="50%"
        left="50%"
        transform="translate(-50%, -50%)"
        zIndex={1}
        spacing={4}
        opacity={loading ? 1 : 0}
        visibility={loading ? "visible" : "hidden"}
        transition="opacity 0.4s ease, visibility 0.4s ease"
        pointerEvents={loading ? "auto" : "none"}
      >
        <Spinner size="lg" color="purple.500" thickness="3px" />
        <Text
          fontSize="md"
          fontWeight="semibold"
          color={textColor}
          sx={{
            textShadow: "0 0 10px rgba(168, 85, 247, 0.8), 0 2px 8px rgba(0, 0, 0, 1), -1px -1px 2px rgba(255,255,255,0.5), 1px 1px 2px rgba(0,0,0,0.8)",
          }}
        >
          {t("mood.connecting")}
        </Text>
      </VStack>
      <Box
        opacity={loading ? 0 : 1}
        transition="opacity 0.5s ease 0.2s"
        h="100%"
        borderRadius="12px"
        overflow="hidden"
      >
        <iframe
          src="https://www.nexbox.top/love"
          style={{
            width: "100%",
            height: "100%",
            border: "none",
          }}
          title={t("mood.title")}
          allow="accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture"
          allowFullScreen
          onLoad={() => setLoading(false)}
        />
      </Box>
    </Box>
  );
}
