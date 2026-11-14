import { Box, HStack, Text, Badge, useColorModeValue } from "@chakra-ui/react";
import { LuChevronDown, LuCheck } from "react-icons/lu";
import { useState, useRef, useEffect, useCallback } from "react";
import { createPortal } from "react-dom";
import { LiquidGlassCard } from "./liquid-glass-card";
import { useThemeColor } from "@/contexts/theme-color-context";

interface SelectOption {
  value: string;
  label: string;
  badge?: string;
}

interface CustomSelectProps {
  value: string;
  onChange: (value: string) => void;
  options: SelectOption[];
  width?: string;
  placeholder?: string;
  direction?: "up" | "down";
}

export function CustomSelect({ 
  value, 
  onChange, 
  options, 
  width = "140px",
  placeholder,
  direction = "down"
}: CustomSelectProps) {
  const [isOpen, setIsOpen] = useState(false);
  const [dropdownPos, setDropdownPos] = useState<{ top?: number; bottom?: number; left: number; width: number } | null>(null);
  const selectRef = useRef<HTMLDivElement>(null);
  const dropdownRef = useRef<HTMLDivElement>(null);
  const scrollTopRef = useRef(0);
  
  const textColor = useColorModeValue("gray.700", "#ffffff");
  const iconColor = useColorModeValue("gray.500", "#999999");
  const dropdownBg = useColorModeValue("white", "#111111");
  const itemBg = useColorModeValue("white", "#111111");
  // 主题色适配：选中项高亮使用主题色
  const { getActiveColor, getHoverColor, getBorderColor } = useThemeColor();
  const itemBgActive = getHoverColor();
  const itemText = useColorModeValue("gray.600", "#cccccc");
  const itemTextActive = useColorModeValue("gray.900", getActiveColor());

  useEffect(() => {
    const handleClickOutside = (event: MouseEvent) => {
      const target = event.target as Node;
      const isClickInsideSelect = selectRef.current?.contains(target);
      const isClickInsideDropdown = dropdownRef.current?.contains(target);
      
      if (!isClickInsideSelect && !isClickInsideDropdown) {
        setIsOpen(false);
      }
    };

    document.addEventListener("mousedown", handleClickOutside);
    return () => document.removeEventListener("mousedown", handleClickOutside);
  }, []);

  // Set initial position when opening
  useEffect(() => {
    if (isOpen && selectRef.current) {
      const rect = selectRef.current.getBoundingClientRect();
      if (direction === "up") {
        setDropdownPos({
          bottom: window.innerHeight - rect.top + 4,
          left: rect.left,
          width: rect.width,
        });
      } else {
        setDropdownPos({
          top: rect.bottom + 4,
          left: rect.left,
          width: rect.width,
        });
      }
    } else {
      setDropdownPos(null);
    }
  }, [isOpen, direction]);

  // Follow scroll container via direct DOM manipulation (no re-render)
  useEffect(() => {
    if (!isOpen || !selectRef.current) return;

    // Find nearest scrollable ancestor
    let scrollContainer: HTMLElement | null = selectRef.current.parentElement;
    while (scrollContainer) {
      const style = window.getComputedStyle(scrollContainer);
      if (style.overflowY === "auto" || style.overflowY === "scroll") {
        break;
      }
      scrollContainer = scrollContainer.parentElement;
    }
    if (!scrollContainer) return;

    scrollTopRef.current = scrollContainer.scrollTop;

    const onScroll = () => {
      const el = dropdownRef.current;
      if (!el || !scrollContainer) return;
      const offset = scrollTopRef.current - scrollContainer.scrollTop;
      el.style.transform = `translateY(${offset}px)`;
    };

    const onResize = () => {
      const el = dropdownRef.current;
      if (!el || !selectRef.current || !scrollContainer) return;
      const rect = selectRef.current.getBoundingClientRect();
      el.style.transform = "";
      scrollTopRef.current = scrollContainer.scrollTop;
      if (direction === "up") {
        el.style.bottom = `${window.innerHeight - rect.top + 4}px`;
        el.style.top = "";
      } else {
        el.style.top = `${rect.bottom + 4}px`;
        el.style.bottom = "";
      }
      el.style.left = `${rect.left}px`;
      el.style.width = `${rect.width}px`;
    };

    scrollContainer.addEventListener("scroll", onScroll);
    window.addEventListener("resize", onResize);
    return () => {
      scrollContainer?.removeEventListener("scroll", onScroll);
      window.removeEventListener("resize", onResize);
    };
  }, [isOpen, direction]);

  const toggleSelect = useCallback(() => {
    setIsOpen((prev) => !prev);
  }, []);

  const selectedOption = options.find((opt) => opt.value === value);
  const displayLabel = selectedOption?.label || placeholder || value;

  return (
    <>
      <Box ref={selectRef} w={width}>
        <LiquidGlassCard
          px={3}
          py={1.5}
          cursor="pointer"
          onClick={toggleSelect}
        >
          <HStack justify="space-between">
            <Text fontSize="sm" color={textColor} noOfLines={1} minW={0}>
              {displayLabel}
            </Text>
            <LuChevronDown
              size={14}
              color={iconColor}
              style={{
                transform: isOpen ? "rotate(180deg)" : "rotate(0deg)",
                transition: "transform 0.2s",
                flexShrink: 0,
              }}
            />
          </HStack>
        </LiquidGlassCard>
      </Box>

      {isOpen && dropdownPos && createPortal(
          <Box
            ref={dropdownRef}
            position="fixed"
            top={dropdownPos.top}
            bottom={dropdownPos.bottom}
            left={dropdownPos.left}
            width={`${dropdownPos.width}px`}
            bg={dropdownBg}
            border="1px solid"
            borderColor={getBorderColor()}
            borderRadius="2xl"
            boxShadow="2xl"
            zIndex={99999}
            maxH="280px"
            overflowY="auto"
            sx={{
              "&::-webkit-scrollbar": {
                width: "6px",
              },
              "&::-webkit-scrollbar-track": {
                background: "transparent",
              },
              "&::-webkit-scrollbar-thumb": {
                background: getActiveColor(),
                borderRadius: "3px",
              },
              "&::-webkit-scrollbar-thumb:hover": {
                background: getHoverColor(),
              },
            }}
          >
            {options.map((option) => (
              <Box
                key={option.value}
                px={3}
                py={2}
                cursor="pointer"
                bg={itemBg}
                color={option.value === value ? itemTextActive : itemText}
                _hover={{ bg: itemBgActive }}
                onClick={() => {
                  onChange(option.value);
                  setIsOpen(false);
                }}
                transition="all 0.15s"
              >
                <HStack justify="space-between">
                  <HStack spacing={2}>
                    <Text fontSize="sm">{option.label}</Text>
                    {option.badge && (
                      <Badge
                        fontSize="0.55rem"
                        colorScheme="purple"
                        variant="subtle"
                        px={1.5}
                        py={0.5}
                        borderRadius="full"
                      >
                        {option.badge}
                      </Badge>
                    )}
                  </HStack>
                  {option.value === value && <LuCheck size={14} color={getActiveColor()} />}
                </HStack>
              </Box>
            ))}
          </Box>,
          document.body
        )}
    </>
  );
}