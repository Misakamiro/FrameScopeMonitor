import {
  Modal,
  ModalOverlay,
  ModalContent,
  ModalHeader,
  ModalBody,
  ModalFooter,
  Button,
  Text,
  Alert,
  AlertIcon,
  Spinner,
} from "@chakra-ui/react";
import { useState, useCallback, useEffect } from "react";
import { invoke } from "@tauri-apps/api/core";

interface PawnioInstallModalProps {
  isOpen: boolean;
  onClose: () => void;
  /** 安装成功后的回调 */
  onSuccess?: () => void;
}

type InstallPhase = "confirm" | "installing" | "success" | "failure";

/**
 * PawnIO 驱动安装对话框
 * 流程：确认 → 安装中 → 完成/失败 → 用户手动重启
 */
export function PawnioInstallModal({ isOpen, onClose, onSuccess }: PawnioInstallModalProps) {
  const [phase, setPhase] = useState<InstallPhase>("confirm");
  const [errorMsg, setErrorMsg] = useState("");

  // 每次打开时重置
  useEffect(() => {
    if (isOpen) {
      setPhase("confirm");
      setErrorMsg("");
    }
  }, [isOpen]);

  const handleInstall = useCallback(async () => {
    setPhase("installing");
    try {
      const result = await invoke<string>("install_pawnio_driver");
      if (result === "already_installed") {
        setPhase("success");
      } else if (result === "success") {
        setPhase("success");
      } else {
        setErrorMsg(result);
        setPhase("failure");
      }
    } catch (e) {
      setErrorMsg(typeof e === "string" ? e : "安装失败");
      setPhase("failure");
    }
  }, []);


  return (
    <Modal isOpen={isOpen} onClose={onClose} closeOnOverlayClick={phase === "failure"} closeOnEsc={phase === "failure"}>
      <ModalOverlay />
      <ModalContent>
        {/* 确认阶段 */}
        {phase === "confirm" && (
          <>
            <ModalHeader>安装 PawnIO 驱动</ModalHeader>
            <ModalBody>
              <Text mb={3}>
                将安装 PawnIO 内核驱动以获取 CPU 温度、风扇转速等更详细的传感器数据。
              </Text>
              <Alert status="info" borderRadius="md">
                <AlertIcon />
                <Text fontSize="sm">
                  安装完成后将自动生效，无需重启系统。
                </Text>
              </Alert>
            </ModalBody>
            <ModalFooter>
              <Button variant="ghost" mr={3} onClick={onClose}>取消</Button>
              <Button colorScheme="blue" onClick={handleInstall}>
                安装
              </Button>
            </ModalFooter>
          </>
        )}

        {/* 安装中 */}
        {phase === "installing" && (
          <>
            <ModalHeader>正在安装 PawnIO 驱动</ModalHeader>
            <ModalBody textAlign="center" py={6}>
              <Spinner size="xl" mb={4} />
              <Text>正在安装驱动，请稍候...</Text>
              <Text fontSize="sm" color="gray.500" mt={2}>
                可能需要管理员权限确认
              </Text>
            </ModalBody>
          </>
        )}

        {/* 安装成功 */}
        {phase === "success" && (
          <>
            <ModalHeader>安装完成</ModalHeader>
            <ModalBody>
              <Text mb={3}>PawnIO 驱动已安装成功。</Text>
              <Alert status="warning" borderRadius="md">
                <AlertIcon />
                <Text fontSize="sm">
                  请重启 NexBox 后，即可获取 CPU 温度等传感器数据。
                </Text>
              </Alert>
            </ModalBody>
            <ModalFooter>
              <Button colorScheme="blue" onClick={() => { onClose(); onSuccess?.(); }}>
                好的
              </Button>
            </ModalFooter>
          </>
        )}

        {/* 安装失败 */}
        {phase === "failure" && (
          <>
            <ModalHeader>安装失败</ModalHeader>
            <ModalBody>
              <Text color="red.400">{errorMsg}</Text>
            </ModalBody>
            <ModalFooter>
              <Button onClick={onClose}>关闭</Button>
            </ModalFooter>
          </>
        )}
      </ModalContent>
    </Modal>
  );
}
