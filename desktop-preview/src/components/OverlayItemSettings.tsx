import { useEffect, useState } from 'react';
import { invoke } from '@tauri-apps/api/core';
import { Button, Checkbox, FormControl, FormLabel, Input, Modal, ModalBody, ModalCloseButton, ModalFooter, ModalHeader, Text, VStack } from "@chakra-ui/react";
import { ModalOverlay } from "@/components/ui/motion-components";
import { ModalContent } from "@/components/ui/motion-components";
import { CustomSelect } from './special/custom-select';
import { OVERLAY_METRICS, type OverlayItemOptions, type OverlaySensor } from '@/lib/overlay-items';

export interface EditableOverlayItem extends OverlayItemOptions { id: string; name: string; custom: boolean }
export function OverlayItemSettings({ item, onClose, onSave, onPasswordSettings }: {
  item: EditableOverlayItem; onClose: () => void;
  onSave: (item: EditableOverlayItem) => Promise<void>; onPasswordSettings: () => void;
}) {
  const [draft, setDraft] = useState(item);
  const [sensors, setSensors] = useState<OverlaySensor[]>([]);
  const [error, setError] = useState('');
  const [saving, setSaving] = useState(false);
  useEffect(() => {
    let active = true;
    const refresh = () => invoke<{ sensor_catalog?: OverlaySensor[] }>('get_overlay_hardware_data')
      .then(data => { if (active) setSensors(data.sensor_catalog || []); })
      .catch(() => { if (active) setSensors([]); });
    void refresh();
    const timer = setInterval(refresh, 2000);
    return () => { active = false; clearInterval(timer); };
  }, []);
  const metric = draft.custom ? draft.metric || '' : draft.id;
  const descriptor = OVERLAY_METRICS[metric];
  const candidates = sensors.filter(sensor => sensor.category === metric && sensor.unit === descriptor?.unit);
  const key = (sensor: { device_id: string; sensor_id: string }) => JSON.stringify([sensor.device_id, sensor.sensor_id]);
  const selected = draft.binding ? key(draft.binding) : '';
  const unavailable = draft.binding && !candidates.some(sensor => key(sensor) === selected);
  const update = (patch: Partial<EditableOverlayItem>) => setDraft(previous => ({ ...previous, ...patch }));
  const save = async () => {
    setSaving(true); setError('');
    try { await onSave(draft); onClose(); }
    catch (reason) { setError(String(reason)); }
    finally { setSaving(false); }
  };
  return <Modal isOpen onClose={onClose} isCentered><ModalOverlay /><ModalContent>
    <ModalHeader>显示项设置</ModalHeader><ModalCloseButton />
    <ModalBody><VStack align="stretch" spacing={4}>
      <FormControl><FormLabel>{draft.custom && !metric ? '文字内容' : '显示名称'}</FormLabel>
        <Input value={draft.name} maxLength={draft.custom ? 160 : 80} onChange={event => update({ name: event.target.value })} /></FormControl>
      {draft.custom && <FormControl><FormLabel>显示内容</FormLabel><CustomSelect ariaLabel="显示内容" width="100%" value={metric}
        onChange={value => update({ metric: value || null, binding: null })}
        options={[{ value: '', label: '纯文本' }, ...Object.entries(OVERLAY_METRICS).map(([value, metric]) => ({ value, label: metric.label }))]} />
      </FormControl>}
      {descriptor ? <FormControl><FormLabel>数据接口 · {descriptor.unit}</FormLabel>
        <CustomSelect ariaLabel="数据接口" width="100%" value={selected} onChange={value => {
          const sensor = candidates.find(candidate => key(candidate) === value);
          update({ binding: sensor ? { category: metric, device_id: sensor.device_id, sensor_id: sensor.sensor_id } : null });
        }} options={[{ value: '', label: '自动汇总（按默认规则选择）' },
          ...(unavailable ? [{ value: selected, label: '已选接口不可用', description: draft.binding?.sensor_id }] : []),
          ...candidates.map(sensor => ({ value: key(sensor),
            label: `${sensor.device_name} · ${sensor.name} · ${sensor.available && sensor.value != null ? `${sensor.value.toFixed(sensor.unit === 'V' ? 3 : ['MHz', 'RPM'].includes(sensor.unit) ? 0 : 1)} ${sensor.unit}` : '不可用'}`,
            description: sensor.sensor_id }))]} />
        <Text fontSize="xs" color="var(--framescope-surface-muted)" mt={2}>手动接口失效后保持选择并显示不可用；仅影响悬浮框。</Text>
      </FormControl> : metric.startsWith('fps') ? <Text fontSize="sm">FPS 与 Low 使用同一采集窗口，接口不可更改。</Text> : null}
      {draft.id === 'delta_password' && <Button onClick={onPasswordSettings}>选择密码地图</Button>}
      <FormControl><FormLabel>字体颜色</FormLabel><Checkbox isChecked={draft.font_color == null}
        onChange={event => update({ font_color: event.target.checked ? null : '#FFFFFF' })}>继承全局</Checkbox>
        {draft.font_color != null && <Input mt={2} type="color" value={draft.font_color} onChange={event => update({ font_color: event.target.value })} />}</FormControl>
      <FormControl><FormLabel>字号</FormLabel><Checkbox isChecked={draft.font_size == null}
        onChange={event => update({ font_size: event.target.checked ? null : 13 })}>继承全局</Checkbox>
        {draft.font_size != null && <Input mt={2} type="number" min={8} max={72} value={draft.font_size}
          onChange={event => update({ font_size: Math.max(8, Math.min(72, Number(event.target.value) || 8)) })} />}</FormControl>
      <FormControl><FormLabel>字重</FormLabel><CustomSelect ariaLabel="字重" width="100%" value={draft.font_bold == null ? 'inherit' : String(draft.font_bold)}
        onChange={value => update({ font_bold: value === 'inherit' ? null : value === 'true' })}
        options={[{ value: 'inherit', label: '继承全局' }, { value: 'false', label: '常规' }, { value: 'true', label: '加粗' }]} /></FormControl>
      <Button variant="ghost" onClick={() => update({ font_color: null, font_size: null, font_bold: null })}>恢复全局字体样式</Button>
      {error && <Text color="red.400">{error}</Text>}
    </VStack></ModalBody><ModalFooter><Button mr={3} onClick={onClose}>取消</Button>
      <Button colorScheme="blue" isLoading={saving} onClick={save}>保存</Button></ModalFooter>
  </ModalContent></Modal>;
}
