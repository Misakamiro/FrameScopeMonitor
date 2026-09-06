import {
  Modal,
  ModalOverlay,
  ModalContent,
  ModalHeader,
  ModalBody,
  ModalFooter,
  ModalCloseButton,
  Button,
  VStack,
  HStack,
  Text,
  Spinner,
  Avatar,
  useColorModeValue,
  Icon,
} from "@chakra-ui/react";
import { useTranslation } from "react-i18next";
import { LogIn, LogOut } from "lucide-react";
import { useThemeColor } from "@/contexts/theme-color-context";
import type { GitCodeLoginStatus } from "@/hooks/use-community-tools";

export function GitCodeLoginDialog({
  isOpen,
  onClose,
  loginStatus,
  onLogin,
  onLogout,
  connecting,
}: {
  isOpen: boolean;
  onClose: () => void;
  loginStatus: GitCodeLoginStatus;
  onLogin: () => Promise<boolean>;
  onLogout: () => Promise<void>;
  connecting: boolean;
}) {
  const { t } = useTranslation();
  const { getActiveColor, getHoverColor, getContrastTextColor } = useThemeColor();
  const titleColor = useColorModeValue("gray.800", "#ffffff");
  const descColor = useColorModeValue("gray.500", "#ffffff");
  const already = loginStatus.logged_in && loginStatus.user;

  const themePrimaryBtn = {
    bg: getActiveColor(),
    color: getContrastTextColor(),
    _hover: { bg: getHoverColor() },
  };

  return (
    <Modal isOpen={isOpen} onClose={onClose} isCentered>
      <ModalOverlay backdropFilter="blur(4px)" />
      <ModalContent bg={useColorModeValue("white", "#000000")}>
        <ModalHeader color={titleColor}>{t("tools.community.loginTitle")}</ModalHeader>
        <ModalCloseButton />
        <ModalBody>
          <VStack spacing={4} align="stretch">
            <Text fontSize="sm" color={descColor}>
              {t("tools.community.loginDesc")}
            </Text>
            {already ? (
              <HStack spacing={3}>
                <Avatar
                  src={already.avatar_data ?? already.avatar_url ?? undefined}
                  name={already.login}
                  bg={getActiveColor()}
                  color={getContrastTextColor()}
                />
                <VStack align="start" spacing={0}>
                  <Text fontWeight="semibold" color={titleColor}>
                    {already.login}
                  </Text>
                  <Text fontSize="xs" color="green.500">
                    {t("tools.community.loggedIn")}
                  </Text>
                </VStack>
              </HStack>
            ) : (
              <HStack spacing={3}>
                <Icon as={LogIn} boxSize={6} />
                <Text fontSize="sm" color={descColor}>
                  {connecting ? t("tools.community.connecting") : t("tools.community.notLoggedIn")}
                </Text>
              </HStack>
            )}
          </VStack>
        </ModalBody>
        <ModalFooter>
          {already ? (
            <Button
              leftIcon={<Icon as={LogOut} boxSize={4} />}
              colorScheme="red"
              variant="ghost"
              onClick={async () => {
                await onLogout();
                onClose();
              }}
            >
              {t("tools.community.logout")}
            </Button>
          ) : (
            <Button
              {...themePrimaryBtn}
              isDisabled={connecting}
              leftIcon={connecting ? <Spinner size="sm" /> : <Icon as={LogIn} boxSize={4} />}
              onClick={async () => {
                await onLogin();
                onClose();
              }}
            >
              {t("tools.community.authorize")}
            </Button>
          )}
        </ModalFooter>
      </ModalContent>
    </Modal>
  );
}