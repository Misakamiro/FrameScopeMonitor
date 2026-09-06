import { useCallback, useState } from "react";
import { invoke } from "@tauri-apps/api/core";
import { save } from "@tauri-apps/plugin-dialog";
import { useDynamicIsland } from "@/components/ui/dynamic-island";
import { useTranslation } from "react-i18next";

/**
 * 硬件报告导出 Hook
 *
 * 在硬件信息页面和悬浮框页面共用。
 * 点击导出 → 弹出文件保存对话框 → 调用后端生成 HTML → 写入文件 → toast 反馈
 */
export function useHardwareReportExport() {
  const toast = useDynamicIsland("file");
  const { t } = useTranslation();
  const [isExporting, setIsExporting] = useState(false);

  const exportReport = useCallback(async () => {
    if (isExporting) return;

    try {
      // 1. 用户选择保存位置
      const defaultName = `NexBox-Hardware-Report-${new Date()
        .toISOString()
        .slice(0, 19)
        .replace(/[:T]/g, "-")}.html`;

      const path = await save({
        filters: [{ name: "HTML", extensions: ["html"] }],
        title: t("hardwareReport.selectPath") || "选择导出位置",
        defaultPath: defaultName,
      });

      if (!path) return; // 用户取消

      // 2. 显示导出中提示
      setIsExporting(true);
      const toastId = "exporting";
      if (!toast.isActive(toastId)) {
        toast({
          id: toastId,
          title: t("hardwareReport.exporting") || "正在生成报告...",
          status: "loading",
          duration: null,
        });
      }

      // 3. 调用后端导出
      const result = await invoke<string>("export_hardware_report", { path });

      // 4. 成功提示
      toast.update(toastId, {
        title: t("hardwareReport.exportSuccess") || "报告导出成功",
        description: result,
        status: "success",
        duration: 5000,
        isClosable: true,
      });
    } catch (error) {
      const errorMsg = String(error);
      if (errorMsg.includes("记录器未启动")) {
        toast({
          title: t("hardwareReport.exportFailed") || "导出失败",
          description: t("hardwareReport.recorderNotStarted") || "记录器未启动，请稍后重试",
          status: "error",
          duration: 5000,
          isClosable: true,
        });
      } else {
        toast({
          title: t("hardwareReport.exportFailed") || "导出失败",
          description: errorMsg,
          status: "error",
          duration: 5000,
          isClosable: true,
        });
      }
    } finally {
      setIsExporting(false);
    }
  }, [isExporting, toast, t]);

  return { exportReport, isExporting };
}
