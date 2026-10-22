import {
  Box,
  Input,
  InputGroup,
  InputLeftElement,
  VStack,
  Text,
  HStack,
  Icon,
  useColorModeValue,
  Kbd,
} from "@chakra-ui/react";
import { Search } from "lucide-react";
import { useState, useEffect, useRef, useCallback, useMemo } from "react";
import { useNavigate } from "react-router-dom";
import { useTranslation } from "react-i18next";
import { useBackground } from "@/contexts/background-context";
import { useThemeColor } from "@/contexts/theme-color-context";
import {
  searchIndex,
  categoryLabels,
  categoryOrder,
  findSearchResults,
  type SearchItem,
} from "@/config/search-index";
import { invoke } from "@tauri-apps/api/core";
import { useDynamicIsland } from "@/components/ui/dynamic-island";

interface GroupedResults {
  [key: string]: SearchItem[];
}

export function GlobalSearch() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const toast = useDynamicIsland("search");
  const { liquidGlassEnabled, liquidGlassBlur } = useBackground();
  const { getActiveColor, getContrastTextColor } = useThemeColor();

  const [isOpen, setIsOpen] = useState(false);
  // keydown 监听 deps 为空，用 ref 读取最新的开关状态
  const isOpenRef = useRef(false);
  isOpenRef.current = isOpen;
  const [query, setQuery] = useState("");
  const [debouncedQuery, setDebouncedQuery] = useState("");
  const [selectedIndex, setSelectedIndex] = useState(0);

  const inputRef = useRef<HTMLInputElement>(null);
  const containerRef = useRef<HTMLDivElement>(null);
  const selectedRef = useRef<HTMLDivElement>(null);
  const debounceTimerRef = useRef<NodeJS.Timeout | null>(null);

  const activeBg = getActiveColor();
  const activeIconColor = getContrastTextColor();

  // 模糊立即生效：页面切换动画期间的 backdrop-filter 关闭由 .page-animating 类统一处理
  const effectiveBlur = liquidGlassEnabled ? liquidGlassBlur : 0;

  const inputBg = useColorModeValue(
    liquidGlassEnabled ? "rgba(255,255,255,0.25)" : "rgba(255,255,255,0.9)",
    liquidGlassEnabled ? "rgba(0,0,0,0.25)" : "rgba(17,17,17,0.95)"
  );
  const inputBorderColor = useColorModeValue(
    liquidGlassEnabled ? "rgba(255,255,255,0.2)" : "rgba(200,200,200,0.3)",
    liquidGlassEnabled ? "rgba(255,255,255,0.1)" : "rgba(51,51,51,0.5)"
  );
  const textColor = useColorModeValue("gray.700", "#ffffff");
  const placeholderColor = useColorModeValue("gray.500", "gray.400");
  const resultItemHoverBg = useColorModeValue("gray.100", "gray.700");
  const categoryColor = useColorModeValue("gray.500", "gray.400");
  const noResultColor = useColorModeValue("gray.400", "gray.500");

  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key === "k") {
        e.preventDefault();
        setIsOpen(true);
        setTimeout(() => inputRef.current?.focus(), 0);
      }
      if (e.key === "Escape" && isOpenRef.current) {
        // 搜索浮层打开时 Esc 只关闭浮层，不触发全局 Esc 返回
        e.stopPropagation();
        setIsOpen(false);
        setQuery("");
      }
    };

    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, []);

  useEffect(() => {
    const handleClickOutside = (e: MouseEvent) => {
      if (containerRef.current && !containerRef.current.contains(e.target as Node)) {
        setIsOpen(false);
        setQuery("");
      }
    };

    document.addEventListener("mousedown", handleClickOutside);
    return () => document.removeEventListener("mousedown", handleClickOutside);
  }, []);

  useEffect(() => {
    if (debounceTimerRef.current) {
      clearTimeout(debounceTimerRef.current);
    }

    debounceTimerRef.current = setTimeout(() => {
      setDebouncedQuery(query);
    }, 150);

    return () => {
      if (debounceTimerRef.current) {
        clearTimeout(debounceTimerRef.current);
      }
    };
  }, [query]);

  const { results, flatResults, resultCategories } = useMemo(() => {
    const matched = findSearchResults(debouncedQuery, searchIndex, t)
      .filter(item => categoryOrder.includes(item.category));
    const grouped: GroupedResults = {};
    // Keep the best matching category first; a broad page keyword must not
    // displace an exact setting match when the user presses Enter.
    const categories = [...new Set(matched.map(item => item.category))];
    matched.forEach(item => { (grouped[item.category] ??= []).push(item); });
    return { results: grouped, resultCategories: categories,
      flatResults: categories.flatMap(category => grouped[category]) };
  }, [debouncedQuery, t]);

  useEffect(() => { setSelectedIndex(0); }, [flatResults]);
  useEffect(() => { selectedRef.current?.scrollIntoView({ block: "nearest" }); }, [selectedIndex, flatResults]);

  const handleSelect = useCallback(
    async (item: SearchItem) => {
      if (item.action === "run-tool" && item.toolId) {
        try {
          await invoke("run_tool", { toolId: item.toolId });
        } catch (error) {
          toast({
            title: t("tools.messages.runFailed"),
            description: String(error),
            status: "error",
            duration: 3000,
            isClosable: true,
          });
        }
      } else {
        navigate(item.path);
      }
      setIsOpen(false);
      setQuery("");
    },
    [navigate, toast, t]
  );

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === "ArrowDown") {
      e.preventDefault();
      setSelectedIndex((prev) => (prev < flatResults.length - 1 ? prev + 1 : prev));
    } else if (e.key === "ArrowUp") {
      e.preventDefault();
      setSelectedIndex((prev) => (prev > 0 ? prev - 1 : 0));
    } else if (e.key === "Enter" && flatResults[selectedIndex]) {
      e.preventDefault();
      handleSelect(flatResults[selectedIndex]);
    }
  };

  const totalResults = flatResults.length;

  return (
    <Box id="global-search" position="relative" ref={containerRef}>
      <InputGroup size="sm" w="180px">
        <InputLeftElement pointerEvents="none">
          <Icon as={Search} boxSize={4} color={placeholderColor} />
        </InputLeftElement>
        <Input
          ref={inputRef}
          role="combobox"
          aria-expanded={isOpen}
          aria-autocomplete="list"
          aria-controls="global-search-results"
          aria-activedescendant={isOpen && flatResults[selectedIndex] ? `search-result-${flatResults[selectedIndex].id}` : undefined}
          placeholder={t("search.placeholder")}
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          onFocus={() => setIsOpen(true)}
          onKeyDown={handleKeyDown}
          bg={inputBg}
          borderColor={inputBorderColor}
          color={textColor}
          borderRadius="lg"
          _placeholder={{ color: placeholderColor }}
          _focus={{
            borderColor: activeBg,
            boxShadow: `0 0 0 1px ${activeBg}`,
          }}
          backdropFilter={`blur(${effectiveBlur}px)`}
          sx={{
            transform: "translateZ(0)",
            WebkitTransform: "translateZ(0)",
            willChange: "auto",
            transition: "backdrop-filter 0.45s cubic-bezier(0.4, 0, 0.2, 1)",
          }}
          pr="60px"
        />
        {!isOpen && (
          <Box position="absolute" right={3} top="50%" transform="translateY(-50%)">
            <HStack spacing={1}>
              <Kbd fontSize="10px" px={1} py={0.5} borderRadius="md" bg={resultItemHoverBg}>
                Ctrl
              </Kbd>
              <Kbd fontSize="10px" px={1} py={0.5} borderRadius="md" bg={resultItemHoverBg}>
                K
              </Kbd>
            </HStack>
          </Box>
        )}
      </InputGroup>

      {isOpen && (
        <Box
          id="global-search-results"
          role="listbox"
          data-search-query={debouncedQuery}
          position="absolute"
          top="calc(100% + 8px)"
          left={0}
          w="320px"
          maxH="400px"
          overflowY="auto"
          bg={inputBg}
          borderRadius="xl"
          border="1px solid"
          borderColor={inputBorderColor}
          boxShadow="2xl"
          backdropFilter={`blur(${effectiveBlur}px)`}
          sx={{
            transform: "translateZ(0)",
            WebkitTransform: "translateZ(0)",
            willChange: "auto",
            transition: "backdrop-filter 0.45s cubic-bezier(0.4, 0, 0.2, 1)",
            "&::-webkit-scrollbar": {
              width: "4px",
            },
            "&::-webkit-scrollbar-track": {
              background: "transparent",
            },
            "&::-webkit-scrollbar-thumb": {
              background: activeBg,
              borderRadius: "2px",
            },
          }}
          zIndex={1000}
          py={2}
        >
          {totalResults === 0 ? (
            <Text px={4} py={3} color={noResultColor} fontSize="sm" textAlign="center">
              {query.trim() ? t("search.noResults") : t("search.typeToSearch") || "输入关键词搜索..."}
            </Text>
          ) : (
            <VStack align="stretch" spacing={1}>
              {resultCategories.map((category) => {
                const items = results[category];
                if (!items || items.length === 0) return null;

                return (
                  <Box key={category}>
                    <Text px={4} py={1} fontSize="xs" fontWeight="medium" color={categoryColor} textTransform="uppercase">
                      {t(categoryLabels[category])}
                    </Text>
                    {items.map((item) => {
                      const globalIndex = flatResults.indexOf(item);
                      const isSelected = globalIndex === selectedIndex;

                      return (
                        <Box
                          key={item.id}
                          id={`search-result-${item.id}`}
                          role="option"
                          aria-selected={isSelected}
                          ref={isSelected ? selectedRef : undefined}
                          px={3}
                          py={2}
                          mx={2}
                          borderRadius="lg"
                          cursor="pointer"
                          bg={isSelected ? activeBg : "transparent"}
                          color={isSelected ? activeIconColor : textColor}
                          onMouseMove={event => {
                            // Keyboard scrolling moves rows under a stationary
                            // pointer; only a real pointer move changes selection.
                            if (event.movementX || event.movementY) setSelectedIndex(globalIndex);
                          }}
                          onClick={() => handleSelect(item)}
                          transition="all 0.15s"
                          _hover={{
                            bg: isSelected ? activeBg : resultItemHoverBg,
                          }}
                        >
                          <HStack spacing={3}>
                            <Icon as={item.icon} boxSize={4} />
                            <Box minW={0}>
                              <Text fontSize="sm" fontWeight="medium">{t(item.nameKey)}</Text>
                              {item.locationKeys && <Text fontSize="xs" opacity={.75}>
                                {item.locationKeys.map(key => t(key)).join(" › ")}
                              </Text>}
                              {item.hintKey && <Text fontSize="xs" opacity={.75}>{t(item.hintKey)}</Text>}
                            </Box>
                          </HStack>
                        </Box>
                      );
                    })}
                  </Box>
                );
              })}
            </VStack>
          )}
        </Box>
      )}
    </Box>
  );
}
