import React from "react";
import { Box, Button, Text, VStack } from "@chakra-ui/react";

type Props = { children: React.ReactNode };
type State = { error: Error | null };

/**
 * Last-resort error boundary around the whole app. Without it a render error
 * unmounts the entire tree and the window shows nothing but a black surface.
 */
export class RootErrorBoundary extends React.Component<Props, State> {
  state: State = { error: null };

  static getDerivedStateFromError(error: Error): State {
    return { error };
  }

  componentDidCatch(error: Error, info: React.ErrorInfo) {
    console.error("RootErrorBoundary caught:", error, info.componentStack);
  }

  render() {
    if (this.state.error) {
      return <Box bg="gray.900" color="white" minH="100vh" display="flex" alignItems="center" justifyContent="center">
        <VStack spacing={4} maxW="560px" px={6}>
          <Text fontSize="xl" fontWeight="bold">界面出现了问题</Text>
          <Text fontSize="sm" opacity={0.8} fontFamily="monospace" wordBreak="break-all">
            {String(this.state.error?.message ?? this.state.error)}
          </Text>
          <Button colorScheme="teal" onClick={() => { this.setState({ error: null }); window.location.reload(); }}>
            重新加载界面
          </Button>
        </VStack>
      </Box>;
    }
    return this.props.children;
  }
}
