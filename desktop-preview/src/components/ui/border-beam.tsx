"use client";

import { Box } from "@chakra-ui/react";

interface BorderBeamProps {
  children: React.ReactNode;
}

export function BorderBeam({ children }: BorderBeamProps) {
  return (
    <Box
      position="relative"
      borderRadius="xl"
      overflow="hidden"
      border="1px solid"
      borderColor="gray.200"
      bg="white"
      boxShadow="sm"
      _before={{
        content: '""',
        position: "absolute",
        top: 0,
        left: 0,
        right: 0,
        height: "2px",
        bg: "linear-gradient(90deg, transparent, #6366f1, transparent)",
        backgroundSize: "200% 100%",
        animation: "border-beam 3s linear infinite",
      }}
      sx={{
        "@keyframes border-beam": {
          "0%": { backgroundPosition: "0% 50%" },
          "100%": { backgroundPosition: "200% 50%" },
        },
      }}
    >
      {children}
    </Box>
  );
}