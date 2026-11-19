import { useEffect, useState } from 'react';
import { listen } from '@tauri-apps/api/event';
import { Modal, ModalOverlay, ModalContent, ModalHeader, ModalBody, Text, VStack, Spinner, Progress } from '@chakra-ui/react';
import { LiquidGlassCard } from './special/liquid-glass-card';
type ExitProgress = { phase: string; message?: string; percent?: number; reports?: Array<{run: string; percent: number; phase: string}> };
export function AppExitProgress() {
  const [state, setState] = useState<ExitProgress>();
  useEffect(() => {
    let disposed = false, stop: (() => void) | undefined;
    void listen<ExitProgress>('app-exit-progress', event => { if (!disposed) setState(event.payload); })
      .then(fn => { if (disposed) fn(); else stop = fn; }).catch(() => undefined);
    return () => { disposed = true; stop?.(); };
  }, []);
  return <Modal isOpen={!!state} onClose={() => undefined} closeOnEsc={false} closeOnOverlayClick={false} isCentered>
    <ModalOverlay/><ModalContent as={LiquidGlassCard} borderRadius="xl"><ModalHeader>正在保存游戏记录</ModalHeader><ModalBody pb={7}>
      <VStack align="stretch" spacing={4}>
        {state?.phase !== 'failed' && <Spinner alignSelf="center"/>}
        <Text>{state?.message ?? (state?.phase === 'retrying-report' ? '正在重试生成报告…' : '停止监测后将自动保存报告并退出。')}</Text>
        {state?.reports?.map(report => <VStack key={report.run} align="stretch" spacing={1}><Text fontSize="sm">{report.phase} · {report.percent}%</Text><Progress value={report.percent} borderRadius="md"/></VStack>)}
        {state?.percent != null && <Progress value={state.percent}/>}
        <Text fontSize="xs" opacity={.65}>原始采集数据将保留。</Text>
      </VStack>
    </ModalBody></ModalContent>
  </Modal>;
}
