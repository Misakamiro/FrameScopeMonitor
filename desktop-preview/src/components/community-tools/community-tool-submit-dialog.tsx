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
  Input,
  Select,
  Box,
  Progress,
  FormControl,
  FormLabel,
  useColorModeValue,
  Icon,
} from "@chakra-ui/react";
import { useCallback, useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { Upload, Link2, Wrench } from "lucide-react";
import { invoke } from "@tauri-apps/api/core";
import { useThemeColor } from "@/contexts/theme-color-context";
import { CustomSelect } from "@/components/special/custom-select";
import type { SubmitCommunityToolParams } from "@/hooks/use-community-tools";

const STANDARD_CATEGORIES = [
  "处理器工具",
  "显卡工具",
  "内存工具",
  "硬盘工具",
  "显示器工具",
  "声卡工具",
  "网卡工具",
  "外设工具",
  "综合工具",
  "系统工具",
  "游戏工具",
  "其他工具",
];

export function CommunityToolSubmitDialog({
  isOpen,
  onClose,
  submitting,
  submitProgress,
  onSubmit,
}: {
  isOpen: boolean;
  onClose: () => void;
  submitting: boolean;
  submitProgress: string | null;
  onSubmit: (params: SubmitCommunityToolParams) => Promise<void>;
}) {
  const { t } = useTranslation();
  const { getActiveColor, getHoverColor, getContrastTextColor } = useThemeColor();
  const titleColor = useColorModeValue("gray.800", "#ffffff");
  const descColor = useColorModeValue("gray.500", "#ffffff");

  const themePrimaryBtn = {
    bg: getActiveColor(),
    color: getContrastTextColor(),
    _hover: { bg: getHoverColor() },
  };
  const themeFocus = { focusBorderColor: getActiveColor() };
  const themedScrollbar = {
    "&::-webkit-scrollbar": { width: "6px" },
    "&::-webkit-scrollbar-thumb": { background: getActiveColor(), borderRadius: "3px" },
    "&::-webkit-scrollbar-track": { background: "transparent" },
  };

  const [method, setMethod] = useState<"zip" | "url">("zip");
  const [zipPath, setZipPath] = useState<string | null>(null);
  const [exes, setExes] = useState<string[]>([]);
  const [launchTarget, setLaunchTarget] = useState<string>("");
  const [downloadUrl, setDownloadUrl] = useState("");
  const [downloadFilter, setDownloadFilter] = useState("");

  const [name, setName] = useState("");
  const [category, setCategory] = useState(STANDARD_CATEGORIES[0]);
  const [description, setDescription] = useState("");
  const [tags, setTags] = useState("");
  const [publisher, setPublisher] = useState("");
  const [homepage, setHomepage] = useState("");
  const [version, setVersion] = useState("");
  const [iconPath, setIconPath] = useState<string | null>(null);
  const [msg, setMsg] = useState<string | null>(null);
  const [picking, setPicking] = useState(false);

  const hasRequired = useMemo(
    () => name.trim().length > 0 && (method === "url" ? downloadUrl.trim().length > 0 : !!zipPath),
    [name, method, downloadUrl, zipPath]
  );

  const reset = useCallback(() => {
    setMethod("zip");
    setZipPath(null);
    setExes([]);
    setLaunchTarget("");
    setDownloadUrl("");
    setDownloadFilter("");
    setName("");
    setCategory(STANDARD_CATEGORIES[0]);
    setDescription("");
    setTags("");
    setPublisher("");
    setHomepage("");
    setVersion("");
    setIconPath(null);
    setMsg(null);
  }, []);

  const pickZip = async () => {
    setPicking(true);
    try {
      const p = await invoke<string | null>("pick_community_package");
      if (p) {
        setZipPath(p);
        const list = await invoke<string[]>("list_zip_entry_exes", { zipPath: p });
        setExes(list);
        setLaunchTarget(list[0] ?? "");
        if (!name.trim()) {
          const base = p.split(/[\\/]/).pop()?.replace(/\.zip$/i, "") ?? "";
          setName(base);
        }
      }
    } catch (e) {
      setMsg(String(e));
    } finally {
      setPicking(false);
    }
  };

  const pickIcon = async () => {
    try {
      const p = await invoke<string | null>("pick_community_icon");
      if (p) {
        setIconPath(p);
      }
    } catch {
      /* ignore */
    }
  };

  const doSubmit = async () => {
    await onSubmit({
      name,
      description,
      category,
      tags,
      zipPath: method === "zip" ? zipPath : null,
      launchTarget: launchTarget || null,
      publisher: publisher || null,
      homepage: homepage || null,
      version: version || null,
      iconPath,
      downloadUrl: method === "url" ? downloadUrl : null,
      downloadFilter: method === "url" ? downloadFilter : null,
    });
    if (!msg) {
      reset();
      onClose();
    }
  };

  return (
    <Modal isOpen={isOpen} onClose={onClose} isCentered scrollBehavior="inside" size="xl">
      <ModalOverlay backdropFilter="blur(4px)" />
      <ModalContent bg={useColorModeValue("white", "#000000")}>
        <ModalHeader color={titleColor}>{t("tools.community.submitTitle")}</ModalHeader>
        <ModalCloseButton isDisabled={submitting} />
        <ModalBody sx={themedScrollbar}>
          <VStack spacing={4} align="stretch">
            {/* 上传方式：主题色切换按钮 */}
            <HStack spacing={3}>
              <Button
                size="sm"
                onClick={() => setMethod("zip")}
                {...(method === "zip"
                  ? themePrimaryBtn
                  : { variant: "outline", borderColor: getActiveColor(), color: getActiveColor() })}
              >
                {method === "zip" && <Icon as={Upload} boxSize={4} mr={1} />}
                {t("tools.community.uploadZip")}
              </Button>
              <Button
                size="sm"
                onClick={() => setMethod("url")}
                {...(method === "url"
                  ? themePrimaryBtn
                  : { variant: "outline", borderColor: getActiveColor(), color: getActiveColor() })}
              >
                {method === "url" && <Icon as={Link2} boxSize={4} mr={1} />}
                {t("tools.community.uploadLink")}
              </Button>
            </HStack>

            {method === "zip" ? (
              <FormControl>
                <FormLabel fontSize="sm">{t("tools.community.zipLabel")}</FormLabel>
                <Button
                  leftIcon={<Icon as={Upload} boxSize={4} />}
                  onClick={pickZip}
                  isLoading={picking}
                  variant="outline"
                  w="full"
                >
                  {zipPath ? zipPath.split(/[\\/]/).pop() : t("tools.community.pickZip")}
                </Button>
                {exes.length > 0 && (
                  <Select
                    mt={2}
                    value={launchTarget}
                    onChange={(e) => setLaunchTarget(e.target.value)}
                    placeholder={t("tools.community.chooseLaunch")}
                  >
                    {exes.map((exe) => (
                      <option key={exe} value={exe}>
                        {exe}
                      </option>
                    ))}
                  </Select>
                )}
              </FormControl>
            ) : (
              <>
                <Input placeholder="https://example.com/tool.zip" value={downloadUrl} onChange={(e) => setDownloadUrl(e.target.value)} {...themeFocus} />
                <Input
                  placeholder={t("tools.community.downloadFilter")}
                  value={downloadFilter}
                  onChange={(e) => setDownloadFilter(e.target.value)}
                  size="sm"
                  {...themeFocus}
                />
                <Text fontSize="xs" color={descColor}>
                  {t("tools.community.linkTip")}
                </Text>
              </>
            )}

            <FormControl isRequired>
              <FormLabel fontSize="sm">{t("tools.community.name")}</FormLabel>
              <Input value={name} onChange={(e) => setName(e.target.value)} {...themeFocus} />
            </FormControl>

            <FormControl>
              <FormLabel fontSize="sm">{t("tools.community.category")}</FormLabel>
              <CustomSelect
                width="full"
                value={category}
                onChange={setCategory}
                options={STANDARD_CATEGORIES.map((c) => ({ value: c, label: c }))}
              />
            </FormControl>

            <FormControl>
              <FormLabel fontSize="sm">{t("tools.community.description")}</FormLabel>
              <Input value={description} onChange={(e) => setDescription(e.target.value)} placeholder={t("tools.community.descriptionPlaceholder")} {...themeFocus} />
            </FormControl>

            <FormControl>
              <FormLabel fontSize="sm">{t("tools.community.tags")}</FormLabel>
              <Input value={tags} onChange={(e) => setTags(e.target.value)} placeholder={t("tools.community.tagsPlaceholder")} {...themeFocus} />
            </FormControl>

            <HStack spacing={3}>
              <FormControl>
                <FormLabel fontSize="sm">{t("tools.community.publisher")}</FormLabel>
                <Input value={publisher} onChange={(e) => setPublisher(e.target.value)} {...themeFocus} />
              </FormControl>
              <FormControl>
                <FormLabel fontSize="sm">{t("tools.community.version")}</FormLabel>
                <Input value={version} onChange={(e) => setVersion(e.target.value)} placeholder="1.0" {...themeFocus} />
              </FormControl>
            </HStack>

            <FormControl>
              <FormLabel fontSize="sm">{t("tools.community.homepage")}</FormLabel>
              <Input value={homepage} onChange={(e) => setHomepage(e.target.value)} placeholder="https://..." {...themeFocus} />
            </FormControl>

            <FormControl>
              <FormLabel fontSize="sm">{t("tools.community.icon")}</FormLabel>
              <Button leftIcon={<Icon as={Wrench} boxSize={4} />} onClick={pickIcon} variant="outline" borderColor={getActiveColor()} color={getActiveColor()}>
                {iconPath ? iconPath.split(/[\\/]/).pop() : t("tools.community.pickIcon")}
              </Button>
            </FormControl>

            {submitting && (
              <Box>
                <Progress size="xs" isIndeterminate />
                <Text fontSize="xs" color={descColor} mt={1}>
                  {submitProgress ?? "..."}
                </Text>
              </Box>
            )}
            {msg && (
              <Text fontSize="xs" color="red.400">
                {msg}
              </Text>
            )}
          </VStack>
        </ModalBody>
        <ModalFooter>
          <Button isDisabled={submitting} variant="ghost" mr={3} onClick={onClose}>
            {t("tools.community.cancel")}
          </Button>
          <Button {...themePrimaryBtn} isDisabled={!hasRequired || submitting} isLoading={submitting} onClick={doSubmit}>
            <Icon as={Link2} boxSize={4} mr={2} />
            {t("tools.community.submitPr")}
          </Button>
        </ModalFooter>
      </ModalContent>
    </Modal>
  );
}